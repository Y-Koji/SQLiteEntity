/****************************************************************
 * SQLiteDataContext
 * 
 *   【概要】
 *   SQLite超簡易ORマッパ
 *   
 *   【機能1】DBファイルの自動生成
 *   コンストラクタの引数で指定している
 *   データベースファイルがない場合、
 *   自動で作成されます。
 *   
 *   【機能2】DBテーブルの自動生成
 *   InsertAsync関数で、データを入れる際、
 *   テーブルが無ければ自動で作成されます。
 *   
 *   【機能3】DB項目自動拡張(削除機能は無し)
 *   エンティティクラスにプロパティを追加した場合、
 *   InsertAsyncでデータを追加する際、
 *   拡張された分が自動でDB項目に反映(追加)されます。
 *   
 *   【サポートされている型】
 *   int, uint, long, ulong, bool,
 *   float, double, string, DateTime
 *   
 *   【備考1】
 *   全てのメソッドは非同期メソッドとなっているため、
 *   同期的に実行したい場合は、Waitの実行やResultプロパティから
 *   結果を取得してください。
 *   
 *   【注意】
 *   「サポートされている型」以外の型のプロパティを保持する
 *   エンティティクラスは利用できません。
 *   
 ***************************************************************/

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SQLiteEntity
{
    public class SQLiteDataContext
    {
        private string FileName { get; } = string.Empty;
        private string ConnectionString { get; } = string.Empty;

        /// <summary>SQLiteDataContextインスタンスを作成します</summary>
        /// <param name="fileName">SQLiteデータソースファイル名</param>
        public SQLiteDataContext(string fileName)
        {
            this.FileName = fileName;
            this.ConnectionString = string.Format("DataSource={0}", fileName);

            Initialize();
        }

        private void Initialize()
        {
            if (!File.Exists(FileName))
            {
                SQLiteConnection.CreateFile(FileName);
            }
        }

        /// <summary>クラス名のテーブルへレコードを登録します</summary>
        /// <param name="obj">登録するインスタンス</param>
        /// <returns>Task</returns>
        public async Task<T> InsertAsync<T>(T obj)
        {
            Type type = typeof(T);
            await CreateTableIfNothing(type);
            string sql = QueryBuilder.BuildInsertQuery(type);
            IReadOnlyCollection<SQLiteParameter> parameters = QueryBuilder.CreateParameters(obj);
            IReadOnlyCollection<string> columns = await GetColumnsAsync(type.Name);

            // DB項目よりプロパティが多ければ、多い分のプロパティを追加する
            foreach (var parameter in parameters)
            {
                if (parameter.ParameterName.ToLower() == "id")
                {
                    // idは自動採番なので、Insert時にはないものとして扱う
                    continue;
                }

                if (!columns.Contains(parameter.ParameterName))
                {
                    await AddColumnAsync(type, type.GetProperty(parameter.ParameterName));
                }
            }

            return await CommandAsync(async command =>
            {
                command.CommandText = sql;
                foreach (var param in parameters)
                {
                    command.Parameters.Add(param);
                }

                await command.ExecuteNonQueryAsync();

                command.CommandText = QueryBuilder.SELECT_LAST_INSERT_ROWID;
                long rowId = (long)await command.ExecuteScalarAsync();

                var prop = 
                    obj.GetType().GetProperties()
                    .Where(x => x.Name.ToLower() == "id" && x.PropertyType == typeof(long)).SingleOrDefault();

                prop?.SetValue(obj, rowId);

                return obj;
            });
        }

        /// <summary>クラス名のテーブルから値を取得します</summary>
        /// <typeparam name="T">取得するDBテーブル名と同等のクラス型</typeparam>
        /// <param name="wheres">検索条件("検索キー " + "条件[=, <>, like]" + "@検索キー", SQLiteParameter("検索キー", "検索値")</param>
        /// <param name="callback">1レコード検索完了毎に実行する関数</param>
        /// <returns>検索結果</returns>
        public async Task<List<T>> SelectAsync<T>(
            IReadOnlyDictionary<string, SQLiteParameter> wheres = null,
            Action<T> callback = null)
        {
            await CreateTableIfNothing(typeof(T));

            if (null == wheres)
            {
                wheres = new Dictionary<string, SQLiteParameter>();
            }

            Type type = typeof(T);
            List<string> columns = await GetColumnsAsync(type.Name);

            return await CommandAsync(async command =>
            {
                List<T> items = new List<T>();

                command.CommandText = QueryBuilder.BuildSelectQuery(type.Name, columns, wheres.Select(x => x.Key).ToList());
                if (null != wheres)
                {
                    foreach (var where in wheres)
                    {
                        command.Parameters.Add(where.Value);
                    }
                }

                var reader = await command.ExecuteReaderAsync();
                IDictionary<string, PropertyInfo> props = type.GetProperties().ToDictionary(x => x.Name.ToLower(), x => x);

                // SELECT結果に存在する全ての項目を、エンティティクラスのインスタンスへコピーする
                while (await reader.ReadAsync())
                {
                    T instance = Activator.CreateInstance<T>();
                    for (int i = 0;i < reader.VisibleFieldCount;i++)
                    {
                        string name = reader.GetName(i);
                        
                        var prop = props[name.ToLower()];

                        await SetValue(instance, prop, reader, i);
                    }

                    callback?.Invoke(instance);

                    items.Add(instance);
                }

                return items;
            });
        }

        public async Task<int> UpdateAsync<T>(T entity)
        {
            await CreateTableIfNothing(typeof(T));

            Type type = typeof(T);
            PropertyInfo idProp = type.GetProperties().Where(x => x.Name.ToLower() == "id").SingleOrDefault();
            if (null == idProp)
            {
                throw new Exception("ID プロパティを保持しないエンティティクラスではDeleteできません");
            }

            return await CommandAsync(async command =>
            {
                command.CommandText = QueryBuilder.BuildUpdateQuery(typeof(T));

                var parameters = QueryBuilder.CreateParameters(entity);
                foreach (var parameter in parameters)
                {
                    command.Parameters.Add(parameter);
                }

                var id = new SQLiteParameter("id", idProp.GetValue(entity));
                command.Parameters.Add(id);

                int count = await command.ExecuteNonQueryAsync();

                return count;
            });
        }

        public async Task<int> DeleteAsync<T>(T entity)
        {
            await CreateTableIfNothing(typeof(T));

            Type type = entity.GetType();
            PropertyInfo idProp = type.GetProperties().Where(x => x.Name.ToLower() == "id").SingleOrDefault();
            if (null == idProp)
            {
                throw new Exception("ID プロパティを保持しないエンティティクラスではDeleteできません");
            }

            return await CommandAsync(async command =>
            {
                command.CommandText = QueryBuilder.BuildDeleteQuery(typeof(T).Name);
                
                var id = new SQLiteParameter("id", idProp.GetValue(entity));
                command.Parameters.Add(id);

                int count = await command.ExecuteNonQueryAsync();

                return count;
            });
        }

        private static async Task SetValue(object instance, PropertyInfo prop, DbDataReader reader, int index)
        {
            #region コピー先のプロパティ型に合わせて、データの取得、コピー処理を行う
            if (prop.PropertyType == typeof(int) ||
                prop.PropertyType == typeof(uint))
            {
                int value = reader.GetInt32(index);
                prop.SetValue(instance, value);
            }
            else if (prop.PropertyType == typeof(long) ||
                prop.PropertyType == typeof(ulong))
            {
                long value = reader.GetInt64(index);
                prop.SetValue(instance, value);
            }
            else if (prop.PropertyType == typeof(bool))
            {
                int value = reader.GetInt32(index);
                if (0 == value)
                {
                    prop.SetValue(instance, false);
                }
                else if (1 == value)
                {
                    prop.SetValue(instance, true);
                }
                else
                {
                    throw new Exception("bool型のデータに0, 1以外の値が見つかりました");
                }
            }
            else if (prop.PropertyType == typeof(float))
            {
                float value = reader.GetFloat(index);
                prop.SetValue(instance, value);
            }
            else if (prop.PropertyType == typeof(double))
            {
                double value = reader.GetDouble(index);
                prop.SetValue(instance, value);
            }
            else if (prop.PropertyType == typeof(string))
            {
                if (!await reader.IsDBNullAsync(index))
                {
                    string value = reader.GetString(index);
                    prop.SetValue(instance, value);
                }
                else
                {
                    prop.SetValue(instance, string.Empty);
                }
            }
            else if (prop.PropertyType == typeof(DateTime))
            {
                if (!await reader.IsDBNullAsync(index))
                {
                    DateTime value = reader.GetDateTime(index);
                    prop.SetValue(instance, value);
                }
                else
                {
                    prop.SetValue(instance, DateTime.MinValue);
                }
            }
            else if (prop.PropertyType == typeof(byte[]))
            {
                if (!await reader.IsDBNullAsync(index))
                {
                    byte[] value = (byte[]) reader[index];
                    prop.SetValue(instance, value);
                }
                else
                {
                    prop.SetValue(instance, DateTime.MinValue);
                }
            }
            #endregion
        }
        
        private async Task CreateTableIfNothing(Type objType)
        {
            if (!await ExistsTableAsync(objType.Name))
            {
                await CommandAsync(async command =>
                {
                    command.CommandText = QueryBuilder.BuildCreateTableQuery(objType);

                    await command.ExecuteNonQueryAsync();
                });
            }
        }

        private async Task<bool> ExistsTableAsync(string name)
        {
            return await CommandAsync(async command =>
            {
                command.CommandText = QueryBuilder.SELECT_TABLE_QUERY;
                command.Parameters.Add(new SQLiteParameter("NAME", name));
                
                long count = (long)await command.ExecuteScalarAsync();

                return 0 < count;
            });
        }

        private async Task AddColumnAsync(Type type, PropertyInfo prop)
        {
            string typeName = type.Name;
            string propName = prop.Name;
            string sql = QueryBuilder.BuildAddColumnQuery(typeName, propName, prop.PropertyType);

            await CommandAsync(async command =>
            {
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            });
        }

        private async Task<List<string>> GetColumnsAsync(string name)
        {
            return await CommandAsync(async command =>
            {
                List<string> columns = new List<string>();

                command.CommandText = QueryBuilder.BuildTableInfoQuery(name);

                var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string value = reader.GetString(1);

                    columns.Add(value);
                }

                return columns;
            });
        }

        public async Task CommandAsync(Action<SQLiteCommand> action)
        {
            using (var conn = new SQLiteConnection(ConnectionString))
            {
                await conn.OpenAsync();
                using (var command = conn.CreateCommand())
                {
                    action?.Invoke(command);
                }

                conn.Close();
            }
        }

        public async Task<T> CommandAsync<T>(Func<SQLiteCommand, Task<T>> func)
        {
            T result = default(T);
            await CommandAsync(async command =>
            {
                if (null == func)
                {
                    return;
                }

                result = await func.Invoke(command);
            });

            return result;
        }
    }
}
