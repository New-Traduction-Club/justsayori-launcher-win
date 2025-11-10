using System.Windows;

namespace justsayo_win
{
    public enum MoveGameResult
    {
        Cancel,
        Move,
        Delete,
        ChangePath
    }

    public partial class MoveGameDialog : Window
    {
        public MoveGameResult Result { get; private set; } = MoveGameResult.Cancel;

        public MoveGameDialog()
        {
            InitializeComponent();
        }

        public void ShowProgress(string actionText)
        {
            QuestionView.Visibility = Visibility.Collapsed;
            ButtonPanel.Visibility = Visibility.Collapsed;
            ProgressView.Visibility = Visibility.Visible;
            ProgressText.Text = actionText;
            this.IsHitTestVisible = false;
        }

        private void MoveButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MoveGameResult.Move;
            DialogResult = true;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MoveGameResult.Delete;
            DialogResult = true;
        }

        private void ChangePathButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MoveGameResult.ChangePath;
            DialogResult = true;
        }
    }
}
