using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteDB;
using TaskDemoAppNetRemoting.Shared;

namespace TaskDemoAppNetRemoting.Server
{
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    public class TodoDatabase : IDisposable
    {
        private static TodoDatabase _singleton;
        
        // ReSharper disable once InconsistentNaming
        private static readonly object _singletonLock = new object();
        
        private LiteDatabase _db;
        private ILiteCollection<Todo> _taskCollection;
        
        private TodoDatabase()
        {
            var dbPath = 
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, 
                    "todo.db");

            _db = new LiteDatabase(dbPath);

            _taskCollection =
                _db.GetCollection<Todo>(BsonAutoId.Guid);
        }
        
        public static TodoDatabase Instance
        {
            get
            {
                if (_singleton == null)
                {
                    lock (_singletonLock)
                    {
                        if (_singleton == null)
                            _singleton = new TodoDatabase();
                    }
                }

                return _singleton;
            }
        }

        public List<Todo> GetAll()
        {
            return _taskCollection.FindAll().ToList();
        }

        public Guid Upsert(Todo item)
        {
            _taskCollection.Upsert(item);
            return item.Id;
        }

        public void Delete(Guid id)
        {
            _taskCollection.Delete(id);
        }

        public void Dispose()
        {
            if (_db != null)
            {
                _db.Dispose();
                _db = null;
            }
        }
    }
}