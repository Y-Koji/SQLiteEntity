using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;

namespace SQLiteEntity
{
    internal static class QueryBuilder
    {
        public static string SELECT_TABLE_QUERY { get; } = "select count(*) from sqlite_master where type='table' and name = @NAME";
        public static string SELECT_LAST_INSERT_ROWID { get; } = "select last_insert_rowid()";

        public static IReadOnlyCollection<Type> SupportTypes { get; } = new List<Type>
        {
            typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(bool),
            typeof(float), typeof(double),
            typeof(string), typeof(DateTime),
        };

        public static IReadOnlyCollection<Type> IntegerTypes { get; } = new List<Type>
        {
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(bool),
        };

        public static IReadOnlyCollection<Type> TextTypes { get; } = new List<Type>
        {
            typeof(string), typeof(DateTime),
        };

        public static IReadOnlyCollection<Type> RealTypes { get; } = new List<Type>
        {
            typeof(float), typeof(double),
        };

        public static string BuildInsertQuery(Type type)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("INSERT INTO {0}(\r\n", type.Name);

            int placeHolderCount = 0;
            foreach (var prop in type.GetProperties())
            {
                if (prop.Name.ToLower() == "id")
                {
                    // idは自動採番のため、Insert時には無いものとして扱う
                    continue;
                }

                if (SupportTypes.Contains(prop.PropertyType))
                {
                    sb.AppendFormat("    {0},\r\n", prop.Name);

                    placeHolderCount++;
                }
            }

            sb.Remove(sb.Length - 3, 3);
            sb.AppendLine("\r\n) VALUES (");

            for (int i = 0; i < placeHolderCount; i++)
            {
                sb.AppendLine("    ?,");
            }

            sb.Remove(sb.Length - 3, 3);
            sb.AppendLine("\r\n);");

            string sql = sb.ToString();

            return sql;
        }

        public static List<SQLiteParameter> CreateParameters(object obj)
        {
            Type type = obj.GetType();
            List<SQLiteParameter> parameters = new List<SQLiteParameter>();

            foreach (var prop in type.GetProperties())
            {
                if (prop.Name.ToLower() == "id")
                {
                    // idは自動採番のため、Insert文には入れない
                    continue;
                }

                if (!SupportTypes.Contains(prop.PropertyType))
                {
                    continue;
                }

                if (prop.PropertyType == typeof(bool))
                {
                    bool bValue = (bool)prop.GetValue(obj);
                    int value = bValue ? 1 : 0;

                    parameters.Add(new SQLiteParameter(prop.Name, value));
                }
                else
                {
                    parameters.Add(new SQLiteParameter(prop.Name, prop.GetValue(obj)));
                }
            }

            return parameters;
        }

        public static string BuildSelectQuery(string name, IReadOnlyCollection<string> columns, IReadOnlyCollection<string> wheres)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SELECT ");
            sb.AppendLine("    id,");

            foreach (var column in columns.Skip(1))
            {
                sb.AppendFormat("    {0},\r\n", column);
            }

            sb.Remove(sb.Length - 3, 3);
            sb.AppendFormat("\r\nFROM {0}\r\n", name);

            if (wheres != null && 0 < wheres.Count)
            {
                sb.AppendLine("WHERE");
                foreach (var where in wheres)
                {
                    sb.AppendFormat("    {0} AND\r\n", where);
                }

                sb.Remove(sb.Length - 6, 6);
                sb.AppendLine(";");
            }
            else
            {
                sb.AppendLine(";");
            }

            string sql = sb.ToString();

            return sql;
        }

        public static string BuildCreateTableQuery(Type type)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("CREATE TABLE {0} (\r\n", type.Name);
            sb.AppendLine("    id INTEGER PRIMARY KEY,");

            foreach (var prop in type.GetProperties())
            {
                // TODO: Getter, Setter 両方ない場合も考慮しないといけない

                if (prop.Name.ToLower() == "id")
                {
                    // idは既に追加しているので、不要
                    continue;
                }

                sb.AppendFormat("    {0} {1},\r\n", prop.Name, TypeToSQLiteTypeName(prop.PropertyType));
            }

            // 末尾の「,」を削除
            sb.Remove(sb.Length - 3, 3);

            sb.AppendLine("\r\n);");

            string sql = sb.ToString();

            return sql;
        }

        public static string BuildUpdateQuery(Type type)
        {
            // ID以外のプロパティ名を取得
            var propNames =
                type.GetProperties()
                .Where(x => x.Name.ToLower() != "id")
                .Select(x => x.Name);
            
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("UPDATE {0}\r\n", type.Name);
            sb.AppendLine("SET");

            foreach (var propName in propNames)
            {
                sb.AppendFormat("    {0} = @{0},\r\n", propName);
            }

            sb.Remove(sb.Length - 3, 3);
            sb.Append("\r\nWHERE\r\n");
            sb.Append("    id = @id");

            string sql = sb.ToString();

            return sql;
        }

        public static string BuildDeleteQuery(string name)
            => $"DELETE FROM {name} WHERE id = @id;";

        public static string BuildTableInfoQuery(string name)
            => $"PRAGMA table_info('{name}');";

        public static string BuildAddColumnQuery(string name, string columnName, Type type)
            => $"ALTER TABLE {name} ADD COLUMN {columnName} {TypeToSQLiteTypeName(type)};";

        public static string TypeToSQLiteTypeName(Type type)
        {
            if (IntegerTypes.Contains(type))
            {
                return "INTEGER";
            }
            else if (TextTypes.Contains(type))
            {
                return "TEXT";
            }
            else if (RealTypes.Contains(type))
            {
                return "REAL";
            }
            else
            {
                throw new Exception($"{type.Name} はサポートされていない型です");
            }
        }
    }
}
