using System.ComponentModel;
using System.Windows.Forms;
using MigratedTaskDemoAppNetRemoting.Shared;
using System.Collections.Generic;
using System.Drawing;
using System.Resources;

namespace MigratedTaskDemoAppNetRemoting.Client
{
    partial class TodoForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            var executingAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            
            var stream = executingAssembly.GetManifestResourceStream("MigratedTaskDemoAppNetRemoting.Client.Resources.add.ico");
            var iconAdd = new System.Drawing.Bitmap(stream); 
            stream.Dispose();

            stream = executingAssembly.GetManifestResourceStream("MigratedTaskDemoAppNetRemoting.Client.Resources.delete.ico");
            var iconDelete = new System.Drawing.Bitmap(stream);
            stream.Dispose();
                
            this.components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Text = "Task List";
            this.Size = new Size(640, 480);
            this.Shown += OnShown;
            // 
            // _toolAdd
            //
            _toolAdd = new ToolStripButton();
            _toolAdd.Name = nameof(_toolAdd);
            _toolAdd.Text = "Add Task";
            _toolAdd.Image = iconAdd;
            _toolAdd.Click += ToolAddOnClick; 
            // _toolDelete
            //
            _toolDelete = new ToolStripButton();
            _toolDelete.Name = nameof(_toolDelete);
            _toolDelete.Text = "Delete Task";
            _toolDelete.Image = iconDelete;
            _toolDelete.Click += ToolDeleteOnClick;
            //
            // _toolBar
            //
            _toolbar = new ToolStrip();
            _toolbar.Name = nameof(_toolbar);
            _toolbar.Dock = DockStyle.Top;
            _toolbar.Items.AddRange(new ToolStripItem[] { _toolAdd, _toolDelete });
            _toolbar.Visible = true;
            _toolbar.Parent = this;
            //
            // _listViewTasks
            //
            _listViewTasks = new ListView();
            _listViewTasks.Name = nameof(_listViewTasks);
            _listViewTasks.Dock = DockStyle.Fill;
            _listViewTasks.Visible = true;
            _listViewTasks.View = View.Details;
            _listViewTasks.Columns.Add("Todo", 600);
            _listViewTasks.CheckBoxes = true;
            _listViewTasks.LabelEdit = true;
            _listViewTasks.AfterLabelEdit += ListViewTasksOnAfterLabelEdit;
            _listViewTasks.ItemChecked += ListViewTasksOnItemChecked;
            _listViewTasks.ItemSelectionChanged += ListViewTasksOnItemSelectionChanged;
            _listViewTasks.MultiSelect = false;
            _listViewTasks.BorderStyle = BorderStyle.None;
            _listViewTasks.Parent = this;
            _listViewTasks.BringToFront();
        }

        private ToolStripButton _toolAdd;
        private ToolStripButton _toolDelete;
        private ToolStrip _toolbar;
        private ListView _listViewTasks;

        #endregion
    }
}