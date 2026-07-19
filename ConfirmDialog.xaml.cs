using System.Windows;

namespace MemoClip
{
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string message, string actionLabel, bool isDanger)
        {
            InitializeComponent();
            TitleBlock.Text = title;
            MessageBlock.Text = message;
            ActionBtn.Content = actionLabel;

            if (isDanger)
            {
                ActionBtn.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(239, 68, 68)); // #EF4444
            }
            else
            {
                ActionBtn.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(79, 110, 247)); // #4F6EF7
            }
        }

        void Action_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
