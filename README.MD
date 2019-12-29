
# SQLiteEntity
SQLiteの.NET ライブラリ(System.Data.SQLite.Core)専用の、簡易ORマッパ。<br />
![](https://i.imgur.com/DIKcQjt.png)

主に利用時に、自動でDBの作成、テーブルの作成、テーブル項目の自動拡張がされるようになっています。

# 機能
- DataContext作成時に、SQLiteのDBファイルを自動生成
- Insert時に自動でテーブルを作成
- Insert時に、追加されたエンティティクラスのプロパティに対応するテーブル項目を自動追加

# サポートされている型
int, uint, long, ulong, bool, <br />
float, double, string, DateTime<br />

※ 「サポートされている型」以外の型のプロパティを保持するエンティティクラスは利用できません。

# CRUDサンプル
![](https://i.imgur.com/CtIyqs5.png) <br />
![](https://i.imgur.com/oi7jsmW.png)
