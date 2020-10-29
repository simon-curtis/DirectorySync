using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.Model;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DirectorySync
{
    /// <summary>
    /// Interaction logic for TextCompare.xaml
    /// </summary>
    public partial class TextCompare : Window
    {
        public TextCompare(string oldText, string newText)
        {
            InitializeComponent();
            var differ = new Differ();
            DiffView.SetDiffModel(differ, oldText, newText);
        }
    }
}
