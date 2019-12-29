using SQLiteEntity;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;

namespace SQLiteEntityTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var context = new SQLiteDataContext("database.sqlite");

            // Create
            var human = context.InsertAsync(new Human
            {
                Name = "Saito",
                Age = 20,
                CreateTime = DateTime.Now,
                IsDeleted = false,
            }).Result;

            // Read
            var result = context.SelectAsync<Human>(new Dictionary<string, SQLiteParameter>
            {
                { "id = @id", new SQLiteParameter("id", human.Id) },
            }).Result.Single();

            // Update
            result.IsDeleted = true;
            result.UpdateTime = DateTime.Now;
            context.UpdateAsync(result).Wait();

            // Delete
            context.DeleteAsync(result).Wait();
        }
    }
}
