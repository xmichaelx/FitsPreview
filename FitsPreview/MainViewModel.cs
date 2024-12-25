using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Media;
using System.Windows;
using System.Windows.Media.Imaging;
using System.IO;
using System.Globalization;
using Microsoft.Win32;

namespace FitsPreview
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private WriteableBitmap _fitsImage;
        private string _filename;
        private byte[] _pixels;
        public WriteableBitmap FitsImage
        {
            get => _fitsImage;
            set
            {
                _fitsImage = value;
                OnPropertyChanged();
            }
        }

        public string Filename
        {
            get => _filename;
            set
            {
                _filename = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            StartBackgroundTask();
        }


        private void Background()
        {
        }

        private void StartBackgroundTask()
        {
            Task.Run(() =>
            {
                var args = Environment.GetCommandLineArgs();
                var end = Encoding.ASCII.GetBytes("END ");
                var beggining = Encoding.ASCII.GetBytes("SIMPLE");
                MemoryStream stream = null;
                Array image = null;

                OpenFileDialog sfd = new();
                sfd.Filter = "Fits files (*.fits)|*.fits|All files (*.*)|*.*";
                sfd.Title = "Select Folder";
                string directory = null;

                if (sfd.ShowDialog() == true)
                {
                    directory = Path.GetDirectoryName(sfd.FileName);
                }


                while (true)
                {
                    var files = Directory.EnumerateFiles(directory, "*.fits").OrderBy(x => x).ToArray();
                    var filename = files[^1];

                    if (Path.GetFileNameWithoutExtension(filename) == Filename)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    Filename = Path.GetFileNameWithoutExtension(filename);



                    var length = new FileInfo(filename).Length;
                    if (stream == null)
                    {
                        stream = new MemoryStream((int)length);
                    }
                    else if (stream.Length != length)
                    {
                        stream.Dispose();
                        stream = new MemoryStream((int)length);
                    }
                    else
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    }

                    using (var s = File.OpenRead(filename))
                    {
                        s.CopyTo(stream);
                    }

                    var data = stream.GetBuffer();

                    if (!data.AsSpan(0, beggining.Length).SequenceEqual(beggining))
                    {
                        break;
                    }

                    var headerLength = 0;
                    var header = new List<KeyValuePair<string, string>>();
                    for (var i = 0; i < length; i += 80)
                    {
                        var line = Encoding.ASCII.GetString(data.AsSpan(i, 80));
                        var key = line.Substring(0, 8).Trim();

                        var value = line.Substring(10, 70).Split('/')[0].Trim();
                        if (key == "CONTINUE")
                        {
                            header[^1] = new KeyValuePair<string, string>(header[^1].Key, header[^1].Value.Replace("&'", value[1..]));
                        }

                        if (data.AsSpan(i, end.Length).SequenceEqual(end))
                        {
                            headerLength = (int)Math.Ceiling((double)i / 2880) * 2880;
                            break;
                        }

                        header.Add(new KeyValuePair<string, string>(key, value));
                    }

                    var width = int.Parse(header.First(x => x.Key.Equals("NAXIS2")).Value);
                    var height = int.Parse(header.First(x => x.Key.Equals("NAXIS1")).Value);
                    var bitpix = int.Parse(header.First(x => x.Key.Equals("BITPIX")).Value);
                    var bzero = 0.0;
                    var bscale = 1.0;
                    if (header.Any(x => x.Key.Equals("BZERO")) && header.Any(x => x.Key.Equals("BSCALE")))
                    {
                        bzero = double.Parse(header.First(x => x.Key.Equals("BZERO")).Value, CultureInfo.InvariantCulture);
                        bscale = double.Parse(header.First(x => x.Key.Equals("BSCALE")).Value, CultureInfo.InvariantCulture);
                    }
                    var elementSize = Math.Abs(bitpix) / 8;

                    var dataLength = width * height * elementSize;
                    var dataSegment = data.AsSpan(headerLength, dataLength);
                    System.Windows.Application.Current?.Dispatcher.Invoke((Delegate)(() =>
                    {
                        if (FitsImage == null || FitsImage.PixelHeight != height || FitsImage.PixelWidth != width)
                        {
                            FitsImage = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
                            _pixels = new byte[width * height];
                        }

                    }));

                    if (elementSize > 1)
                    {
                        for (var i = 0; i < dataLength; i += elementSize)
                        {
                            dataSegment.Slice(i, elementSize).Reverse(); // reverse bytes for big endian
                        }
                    }

                    switch (bitpix)
                    {
                        case 8:
                            if (image is not byte[,] byteArray || byteArray.GetLength(0) != width || byteArray.GetLength(1) != height)
                            {
                                image = new byte[width, height];
                            }

                            Buffer.BlockCopy(data, headerLength, image, 0, dataLength);
                            break;
                        case 16:
                            if (image is not short[,] shortArray || shortArray.GetLength(0) != width || shortArray.GetLength(1) != height)
                            {
                                image = new short[width, height];
                            }

                            Buffer.BlockCopy(data, headerLength, image, 0, dataLength);

                            if (image is short[,] shortArray1)
                            {
                                for (int x = 0; x < height; x++)
                                {
                                    for (int y = 0; y < width; y++)
                                    {
                                        shortArray1[y,x] = (short)(bscale * shortArray1[y, x] + bzero);
                                    }
                                }
                            }
                            
                            break;
                        case 32:
                            if (image is not int[,] intArray || intArray.GetLength(0) != width || intArray.GetLength(1) != height)
                            {
                                image = new int[width, height];
                            }

                            Buffer.BlockCopy(data, headerLength, image, 0, dataLength);
                            break;
                        case 64:
                            if (image is not long[,] longArray || longArray.GetLength(0) != width || longArray.GetLength(1) != height)
                            {
                                image = new long[width, height];
                            }

                            Buffer.BlockCopy(data, headerLength, image, 0, dataLength);
                            break;
                        case -32:
                            if (image is not float[,] floatArray || floatArray.GetLength(0) != width || floatArray.GetLength(1) != height)
                            {
                                image = new float[width, height];
                            }

                            Buffer.BlockCopy(data, headerLength, image, 0, dataLength);
                            break;
                        case -64:
                            if (image is not double[,] doubleArray || doubleArray.GetLength(0) != width || doubleArray.GetLength(1) != height)
                            {
                                image = new double[width, height];
                            }

                            Buffer.BlockCopy(data, headerLength, image, 0, dataLength);
                            break;
                    }


                    Utils.Scale(image, _pixels);
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        FitsImage.WritePixels(new Int32Rect(0, 0, width, height), _pixels, width, 0);
                    });

                }
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
