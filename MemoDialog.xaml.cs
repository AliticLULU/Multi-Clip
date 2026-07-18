using System;
using System.Windows;

namespace MemoClip
{
    public partial class MemoDialog : Window
    {
        public string MemoKey
        {
            get { return (KeyBox.Text ?? "").Trim(); }
            set { KeyBox.Text = value ?? ""; }
        }

        public string MemoValue
        {
            get { return ValueBox.Text ?? ""; }
            set { ValueBox.Text = value ?? ""; }
        }

        public MemoDialog(string title, string initialKey, string initialValue)
        {
            InitializeComponent();
            this.Title = title;
            KeyBox.Text = initialKey ?? "";
            ValueBox.Text = initialValue ?? "";
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(KeyBox.Text))
            {
                MessageBox.Show("Please enter a key name.", "MemoClip",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                KeyBox.Focus();
                return;
            }
            this.DialogResult = true;
            this.Close();
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (string.IsNullOrEmpty(KeyBox.Text))
                KeyBox.Focus();
            else
                ValueBox.Focus();
        }
    }
}
