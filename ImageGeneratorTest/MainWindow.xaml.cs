using ImageGeneration;
using System.ComponentModel;
using System.Dynamic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ImageGeneratorTest
{
    public class Person : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged メンバ  

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion

        private string _name;
        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                if (_name == value)
                {
                    return;
                }
                _name = value;
                OnPropertyChanged("Name");
            }
        }

        private string _message;
        public string Message
        {
            get
            {
                return _message;
            }
            set
            {
                if (_message == value)
                {
                    return;
                }
                _message = value;
                OnPropertyChanged("Message");
            }
        }

        private string _xaml;
        public string Xaml
        {
            get
            {
                return _xaml;
            }
            set
            {
                if (_xaml == value)
                {
                    return;
                }
                _xaml = value;
                OnPropertyChanged("Xaml");
            }
        }
    }

    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private Person _person;

        private const string xamlPrefix = "<UserControl\n xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\"\n xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"\n mc:Ignorable=\"d\"  Width=\"300\"  Height=\"300\">";
        private const string xamlPostfix = "</UserControl>";

        public MainWindow()
        {
            InitializeComponent();
            _person = new Person {
                Name = "田中　太郎",
                Message = "おめでとうございます！",
                Xaml = "<Grid x:Name=\"LayoutRoot\"  HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\">\n" +
                "    <StackPanel>\n" + 
                "        <TextBox Text = \"@Model.Text\" TextWrapping = \"Wrap\" Foreground = \"Blue\" Background = \"White\" />\n" +
                "        <TextBox Text = \"@Model.Name\" TextWrapping = \"Wrap\" Foreground = \"#FF0054FF\" Background = \"#FF94FF8B\" />\n" +
                "    </StackPanel>\n" +
                "</Grid>"
            };
            DataContext = _person;
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {

            // テキストを画像化
            dynamic viewModel = new ExpandoObject();
            viewModel.Name = textBoxName.Text;
            viewModel.Text = textBoxMessage.Text;
            string template = xamlPrefix + textBoxXaml.Text + xamlPostfix;
            var generateImage = ImageGenerator.GenerateImage(template, viewModel);

            BitmapImage biImg = new BitmapImage();
            MemoryStream ms = new MemoryStream(generateImage);
            biImg.BeginInit();
            biImg.StreamSource = ms;
            biImg.EndInit();

            // 画像を表示
            generateImageView.Source = biImg;

        }
    }
}
