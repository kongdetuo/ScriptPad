using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ScriptPad
{
    /// <summary>
    /// AddReferenceWindow.xaml 的交互逻辑
    /// </summary>
    public partial class AddReferenceWindow : Window
    {
        private CsScript script;

        public AddReferenceWindow(CsScript script)
        {
            this.script = script;
            InitializeComponent();
            SearchDll();
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            
        }

        void SearchDll()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            path += @"\Microsoft.NET\Framework\";

            var dir = new DirectoryInfo(path);
            var subdir = dir.GetDirectories().Where(d => !d.Name.Contains("X")).LastOrDefault();
            if (subdir != null)
            {
                var dic = subdir.GetFiles().Where(p => p.Extension == ".dll").ToDictionary(p => p.Name);

                foreach (var item in dic.Keys.OrderBy(p => p.Substring(0, p.Length - 4)))
                {
                    var cb = new CheckBox();
                    cb.Content = item;
                    cb.IsThreeState = false;
                    cb.IsChecked = script.References.FirstOrDefault(r => r.Path == dic[item].FullName) != null;
                    cb.ToolTip = dic[item].FullName;
                    cb.Checked += (sender, e) =>
                    {
                        var cb1 = sender as CheckBox;
                        if (cb1.IsChecked.Value)
                        {
                            script.AddReference(dic[item].FullName);
                        }
                        else
                        {
                            script.RemoveReference(dic[item].FullName);
                        }
                    };
                    ReferenceList.Items.Add(cb);
                }
            }
            else
            {
            }
        }
    }
}
