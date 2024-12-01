using System;
using System.Buffers.Binary;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace GaulBlur.App;

public partial class MainWindow : Window
{
    private BitmapImage? _blurredBitmapImage;
    private BitmapImage? _normalBitmapImage;

    private Uri? _fileUri;

    private const double MoveAmount = 10;
    private const int GaussRadiusIncrement = 1;
    private int _gaussRadious = 5;

    private readonly SerialPort _port;
    private readonly object _syncObject = new();

    public MainWindow()
    {
        InitializeComponent();

        Canvas.SetLeft(LeftGlass, 500);
        Canvas.SetTop(LeftGlass, 100);
        Canvas.SetLeft(RightGlass, 300);
        Canvas.SetTop(RightGlass, 100);

        _port = new SerialPort("COM12", 115200);
        _port.DataReceived += DataReceivedHandler;
    }

    private void MenuItem_Open_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new()
        {
            Title = "Select an Image",
            Filter = "Image files (*.png;*.jpeg)|*.png;*.jpeg|All files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() != true) return;

        _fileUri = new Uri(openFileDialog.FileName);
        _blurredBitmapImage = new BitmapImage(_fileUri);
        var gauss = new GaussianBlur(_blurredBitmapImage);
        MainImageView.Source = gauss.Process(_gaussRadious);

        _normalBitmapImage = new BitmapImage(_fileUri);
        OverlapImageView.Source = _normalBitmapImage;

        MainImageView.Loaded += (sender, args) => { UpdateClip(); };
        OverlapImageView.Loaded += (sender, args) => { UpdateClip(); };

        //_port.DiscardInBuffer();
        //_port.Open();
    }

    private void IncreaseBlur()
    {
        _gaussRadious += GaussRadiusIncrement;
        UpdateBlur();
    }

    private void DecreaseBlur()
    {
        _gaussRadious -= GaussRadiusIncrement;
        UpdateBlur();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.D1:
                IncreaseBlur();
                break;
            case Key.D2:
                DecreaseBlur();
                return;
            case Key.Left:
                MoveGlass(LeftGlass, -MoveAmount, 0);
                MoveGlass(RightGlass, -MoveAmount, 0);
                break;
            case Key.Right:
                MoveGlass(LeftGlass, MoveAmount, 0);
                MoveGlass(RightGlass, MoveAmount, 0);
                break;
            case Key.Up:
                MoveGlass(LeftGlass, 0, -MoveAmount);
                MoveGlass(RightGlass, 0, -MoveAmount);
                break;
            case Key.Down:
                MoveGlass(LeftGlass, 0, MoveAmount);
                MoveGlass(RightGlass, 0, MoveAmount);
                break;
        }

        UpdateClip();
    }

    private void UpdateBlur()
    {
        if (_fileUri is null)
        {
            return;
        }

        _blurredBitmapImage = new BitmapImage(_fileUri);
        var gauss = new GaussianBlur(_blurredBitmapImage);
        MainImageView.Source = gauss.Process(_gaussRadious);
    }

    private void MoveGlass(Ellipse glass, double offsetX, double offsetY)
    {
        double currentLeft = Canvas.GetLeft(glass);
        double currentTop = Canvas.GetTop(glass);
        Canvas.SetLeft(glass, currentLeft + offsetX);
        Canvas.SetTop(glass, currentTop + offsetY);
    }

    private void UpdateClip()
    {
        var leftGlassCenter = new Point(Canvas.GetLeft(LeftGlass) + LeftGlass.Width / 2,
            Canvas.GetTop(LeftGlass) + LeftGlass.Height / 2);
        var rightGlassCenter = new Point(Canvas.GetLeft(RightGlass) + RightGlass.Width / 2,
            Canvas.GetTop(RightGlass) + RightGlass.Height / 2);

        var glass1 = new EllipseGeometry(leftGlassCenter, LeftGlass.Width / 2, LeftGlass.Height / 2);
        var glass2 = new EllipseGeometry(rightGlassCenter, RightGlass.Width / 2, RightGlass.Height / 2);

        var glasses = new CombinedGeometry(GeometryCombineMode.Union, glass1, glass2);

        var background = new RectangleGeometry(new Rect(0, 0, MainImageView.ActualWidth, MainImageView.ActualHeight));

        var fullPicture = new CombinedGeometry(GeometryCombineMode.Exclude, background, glasses);

        MainImageView.Clip = fullPicture;
    }

    private void DataReceivedHandler(object data, SerialDataReceivedEventArgs args)
    {
        const int intSize = 4;

        lock (_syncObject)
        {
            var count = _port.BytesToRead;
            var intsCount = count / intSize;

            byte[] byteArray = new byte[intsCount * 4];
            _port.Read(byteArray, 0, intsCount * 4);

            Span<byte> byteSpan = byteArray.AsSpan();

            Console.WriteLine($"Unread count: {count - intsCount * 4}");

            for (int i = 0; i < intsCount * 4; i += 4)
            {
                var value = BinaryPrimitives.ReadInt32LittleEndian(byteSpan.Slice(i));
                Console.WriteLine($"Values: {value}");
                Console.WriteLine($"HEX: {value:x8}");

                if (true)
                {
                    UpdateClip();
                }
                else
                {
                    if (true)
                    {
                        IncreaseBlur(); 
                    }
                    else
                    {
                        DecreaseBlur();
                    }
                }
            }
        }
    }
}