using System;
using System.Collections.Generic;
using TaskDemoAppNetRemoting.Shared;

namespace TaskDemoAppNetRemoting.Server
{
    public class TodoService : MarshalByRefObject, ITodoService
    {
        public List<Todo> GetTodoList()
        {
            var db = TodoDatabase.Instance;
            return db.GetAll();
        }

        public Todo SaveTodo(Todo item)
        {
            var db = TodoDatabase.Instance;
            db.Upsert(item);
            return item;
        }

        public void DeleteTodo(Guid id)
        {
            var db = TodoDatabase.Instance;
            db.Delete(id);
        }
    }
}