using System;
using System.Buffers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace GaulBlur.App;

public class GaussianBlur
{
    private readonly int[] _alpha;
    private readonly int[] _red;
    private readonly int[] _green;
    private readonly int[] _blue;

    private readonly int _width;
    private readonly int _height;

    private readonly ParallelOptions _pOptions = new() { MaxDegreeOfParallelism = 16 };

    private readonly ArrayPool<int> _arrayPool = ArrayPool<int>.Shared;

    public GaussianBlur(BitmapImage image)
    {
        _width = image.PixelWidth;
        _height = image.PixelHeight;

        var pixelData = _arrayPool.Rent(_width * _height);
        image.CopyPixels(new Int32Rect(0, 0, _width, _height), pixelData, _width * 4, 0);
        
        _alpha = _arrayPool.Rent(_width * _height);
        _red = _arrayPool.Rent(_width * _height);
        _green = _arrayPool.Rent(_width * _height);
        _blue = _arrayPool.Rent(_width * _height);

        Parallel.For(0, pixelData.Length, i =>
        {
            _alpha[i] = (pixelData[i] >> 24) & 0xff;
            _red[i] = (pixelData[i] >> 16) & 0xff;
            _green[i] = (pixelData[i] >> 8) & 0xff;
            _blue[i] = pixelData[i] & 0xff;
        });
        
        _arrayPool.Return(pixelData);
    }

    public BitmapSource Process(int radial)
    {
        var newAlpha = _arrayPool.Rent(_width * _height);
        var newRed = _arrayPool.Rent(_width * _height);
        var newGreen = _arrayPool.Rent(_width * _height);
        var newBlue = _arrayPool.Rent(_width * _height);
        var dest = _arrayPool.Rent(_width * _height);

        Parallel.Invoke(
            () => gaussBlur_4(_alpha, newAlpha, radial),
            () => gaussBlur_4(_red, newRed, radial),
            () => gaussBlur_4(_green, newGreen, radial),
            () => gaussBlur_4(_blue, newBlue, radial));

        Parallel.For(0, dest.Length, i =>
        {
            newAlpha[i] = Math.Clamp(newAlpha[i], 0, 255);
            newRed[i] = Math.Clamp(newRed[i], 0, 255);
            newGreen[i] = Math.Clamp(newGreen[i], 0, 255);
            newBlue[i] = Math.Clamp(newBlue[i], 0, 255);

            dest[i] = (newAlpha[i] << 24) | (newRed[i] << 16) | (newGreen[i] << 8) | (newBlue[i]);
        });

        var bitmapSource = BitmapSource.Create(_width, _height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            dest,
            _width * 4);
        
        _arrayPool.Return(newAlpha);
        _arrayPool.Return(newRed);
        _arrayPool.Return(newGreen);
        _arrayPool.Return(newBlue);
        _arrayPool.Return(dest);
        
        _arrayPool.Return(_alpha);
        _arrayPool.Return(_red);
        _arrayPool.Return(_green);
        _arrayPool.Return(_blue);

        return bitmapSource;
    }

    private void gaussBlur_4(int[] source, int[] dest, int r)
    {
        var bxs = BoxesForGauss(r, 3);
        boxBlur_4(source, dest, _width, _height, (bxs[0] - 1) / 2);
        boxBlur_4(dest, source, _width, _height, (bxs[1] - 1) / 2);
        boxBlur_4(source, dest, _width, _height, (bxs[2] - 1) / 2);
    }

    private static Span<int> BoxesForGauss(int sigma, int n)
    {
        var wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
        var wl = (int)Math.Floor(wIdeal);
        if (wl % 2 == 0) wl--;
        var wu = wl + 2;

        var mIdeal = (double)(12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4 * wl - 4);
        var m = Math.Round(mIdeal);

        var sizes = new int[n];
        for (var i = 0; i < n; i++) sizes[i] = (i < m ? wl : wu);
        return sizes.AsSpan();
    }

    private void boxBlur_4(int[] source, int[] dest, int w, int h, int r)
    {
        for (var i = 0; i < source.Length; i++) dest[i] = source[i];
        boxBlurH_4(dest, source, w, h, r);
        boxBlurT_4(source, dest, w, h, r);
    }

    private void boxBlurH_4(int[] source, int[] dest, int w, int h, int r)
    {
        var iar = (double)1 / (r + r + 1);
        Parallel.For(0, h, _pOptions, i =>
        {
            var ti = i * w;
            var li = ti;
            var ri = ti + r;
            var fv = source[ti];
            var lv = source[ti + w - 1];
            var val = (r + 1) * fv;
            for (var j = 0; j < r; j++) val += source[ti + j];
            for (var j = 0; j <= r; j++)
            {
                val += source[ri++] - fv;
                dest[ti++] = (int)Math.Round(val * iar);
            }

            for (var j = r + 1; j < w - r; j++)
            {
                val += source[ri++] - dest[li++];
                dest[ti++] = (int)Math.Round(val * iar);
            }

            for (var j = w - r; j < w; j++)
            {
                val += lv - source[li++];
                dest[ti++] = (int)Math.Round(val * iar);
            }
        });
    }

    private void boxBlurT_4(int[] source, int[] dest, int w, int h, int r)
    {
        var iar = (double)1 / (r + r + 1);
        Parallel.For(0, w, _pOptions, i =>
        {
            var ti = i;
            var li = ti;
            var ri = ti + r * w;
            var fv = source[ti];
            var lv = source[ti + w * (h - 1)];
            var val = (r + 1) * fv;
            for (var j = 0; j < r; j++) val += source[ti + j * w];
            for (var j = 0; j <= r; j++)
            {
                val += source[ri] - fv;
                dest[ti] = (int)Math.Round(val * iar);
                ri += w;
                ti += w;
            }

            for (var j = r + 1; j < h - r; j++)
            {
                val += source[ri] - source[li];
                dest[ti] = (int)Math.Round(val * iar);
                li += w;
                ri += w;
                ti += w;
            }

            for (var j = h - r; j < h; j++)
            {
                val += lv - source[li];
                dest[ti] = (int)Math.Round(val * iar);
                li += w;
                ti += w;
            }
        });
    }
}