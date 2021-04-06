using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MigratedTaskDemoAppNetRemoting.Shared;

namespace MigratedTaskDemoAppNetRemoting.Client
{
    public partial class TodoForm : Form
    {
        private readonly ITodoService _todoServiceProxy;
        private List<Todo> _taskList;
        
        public TodoForm()
        {
            InitializeComponent();
            
            _todoServiceProxy = ServiceProxyHelper.GetTaskServiceProxy();
            _taskList = new List<Todo>();
        }

        private void LoadTasksFromServer()
        {
            _taskList = _todoServiceProxy.GetTodoList();
        }

        private void FillTaskList()
        {
            _listViewTasks.Items.Clear();

            foreach (var todo in _taskList)
            {
                CreateListViewItem(todo);
            }
        }

        private ListViewItem CreateListViewItem(Todo todo)
        {
            var listViewItem =
                new ListViewItem()
                {
                    Checked = todo.Completed,
                    Text = todo.Description,
                    Name = todo.Id.ToString()
                };

            _listViewTasks.Items.Add(listViewItem);
            return listViewItem;
        }

        private void OnShown(object sender, EventArgs e)
        {
            LoadTasksFromServer();
            FillTaskList();
        }

        private void ToolAddOnClick(object sender, EventArgs e)
        {
            var todo = 
                new Todo()
                {
                    Id = Guid.NewGuid(),
                    Description = "<New>"
                };
            
            _taskList.Add(todo);
            var listViewItem = CreateListViewItem(todo);

            listViewItem.Selected = true;
        }

        private void ToolDeleteOnClick(object sender, EventArgs e)
        {
            if (_listViewTasks.SelectedItems.Count == 0)
                return;

            var selectedItem = _listViewTasks.SelectedItems[0];
            var selectedTodo = GetTodoByListViewItem(selectedItem);

            _taskList.Remove(selectedTodo);
            _listViewTasks.Items.Remove(selectedItem);
            
            _todoServiceProxy.DeleteTodo(selectedTodo.Id);
        }

        private Todo GetTodoByListViewItem(ListViewItem listViewItem)
        {
            if (listViewItem == null)
                return null;
            
            var id = new Guid(listViewItem.Name);

            return _taskList.FirstOrDefault(todo => todo.Id.Equals(id));
        }

        private void ListViewTasksOnAfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            var todo = GetTodoByListViewItem(_listViewTasks.Items[e.Item]);
            
            if (todo == null)
                return;
            
            todo.Description = e.Label;

            _todoServiceProxy.SaveTodo(todo);
        }

        private void ListViewTasksOnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            var todo = GetTodoByListViewItem(e.Item);
            todo.Completed = e.Item.Checked;
            
            _todoServiceProxy.SaveTodo(todo);
        }

        private void ListViewTasksOnItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (_listViewTasks.SelectedItems.Count == 0)
                return;

            var selectedItem = _listViewTasks.SelectedItems[0];
            selectedItem.BeginEdit();
        }
    }
}