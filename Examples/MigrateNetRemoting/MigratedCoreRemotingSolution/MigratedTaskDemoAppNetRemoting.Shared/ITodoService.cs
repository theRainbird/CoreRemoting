using System;
using System.Collections.Generic;

namespace MigratedTaskDemoAppNetRemoting.Shared
{
    public interface ITodoService
    {
        List<Todo> GetTodoList();

        Todo SaveTodo(Todo item);

        void DeleteTodo(Guid id);
    }
}