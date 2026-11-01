using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using ImageMagick;

namespace Donut;

public enum PdfMode
{
    Separate,
    Merge
}

public sealed class FileConverter
{
    public string Convert(string sourcePath, OutputFormat format, PdfMode pdfMode = PdfMode.Separate)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The source file could not be read.", sourcePath);

        string extension = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
        string destination = CreateDestinationPath(sourcePath, format);

        if (extension == format.Extension())
        {
            File.Copy(sourcePath, destination);
            return destination;
        }

        if (format is OutputFormat.Webp or OutputFormat.Heic)
            return ConvertWithMagick(sourcePath, format, destination);

        using var source = new Bitmap(sourcePath);
        using var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.White);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        if (format == OutputFormat.Pdf)
        {
            WritePdf(output, source.HorizontalResolution, destination);
            return destination;
        }

        ImageCodecInfo codec = format switch
        {
            OutputFormat.Jpeg => GetCodec(ImageFormat.Jpeg),
            OutputFormat.Png => GetCodec(ImageFormat.Png),
            OutputFormat.Tiff => GetCodec(ImageFormat.Tiff),
            _ => throw new NotSupportedException()
        };

        if (format == OutputFormat.Jpeg)
        {
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality,
                100L);
            output.Save(destination, codec, parameters);
        }
        else
        {
            output.Save(destination, codec, null);
        }

        return destination;
    }

    public string ConvertPdf(IReadOnlyList<string> sourcePaths, PdfMode mode)
    {
        if (sourcePaths.Count == 0)
            throw new ArgumentException("At least one source image is required.", nameof(sourcePaths));

        if (mode == PdfMode.Separate)
            throw new ArgumentException("Separate PDF conversion must be performed one file at a time.", nameof(mode));

        string destination = CreateDestinationPath(sourcePaths[0], OutputFormat.Pdf, "_merged");
        var images = new MagickImageCollection();
        try
        {
            foreach (string sourcePath in sourcePaths)
            {
                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException("The source file could not be read.", sourcePath);

                var image = new MagickImage(sourcePath)
                {
                    BackgroundColor = MagickColors.White,
                    Density = new Density(96)
                };
                images.Add(image);
            }

            images.Write(destination, MagickFormat.Pdf);
            return destination;
        }
        finally
        {
            images.Dispose();
        }
    }

    private static string ConvertWithMagick(string sourcePath, OutputFormat format, string destination)
    {
        using var image = new MagickImage(sourcePath)
        {
            BackgroundColor = MagickColors.White
        };
        if (format == OutputFormat.Heic)
            image.Settings.SetDefine(MagickFormat.Heic, "heic:speed", "6");
        image.Format = format == OutputFormat.Webp ? MagickFormat.WebP : MagickFormat.Heic;
        image.Write(destination);
        return destination;
    }

    // Windows has no PDF encoder, so write a minimal single-page PDF directly:
    // the image is embedded as a JPEG (DCTDecode) on a page sized to the image.
    private static void WritePdf(Bitmap image, float dpi, string destination)
    {
        byte[] jpeg;
        using (var buffer = new MemoryStream())
        {
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality,
                95L);
            image.Save(buffer, GetCodec(ImageFormat.Jpeg), parameters);
            jpeg = buffer.ToArray();
        }

        if (dpi < 1)
            dpi = 96f;
        string width = (image.Width * 72.0 / dpi).ToString("0.##", CultureInfo.InvariantCulture);
        string height = (image.Height * 72.0 / dpi).ToString("0.##", CultureInfo.InvariantCulture);
        string content = $"q {width} 0 0 {height} 0 0 cm /Im0 Do Q";

        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        var offsets = new List<long>();

        void Write(string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            file.Write(bytes, 0, bytes.Length);
        }

        void BeginObject()
        {
            offsets.Add(file.Position);
            Write($"{offsets.Count} 0 obj\n");
        }

        Write("%PDF-1.4\n");

        BeginObject();
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject();
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        BeginObject();
        Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] " +
              "/Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        BeginObject();
        Write($"<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} " +
              $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        file.Write(jpeg, 0, jpeg.Length);
        Write("\nendstream\nendobj\n");

        BeginObject();
        Write($"<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");

        long xref = file.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
            Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static ImageCodecInfo GetCodec(ImageFormat format) =>
        ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == format.Guid);

    private static string CreateDestinationPath(
        string sourcePath,
        OutputFormat format,
        string baseNameSuffix = "")
    {
        string directory = Path.GetDirectoryName(sourcePath)
            ?? throw new IOException("The source directory could not be resolved.");
        string baseName = Path.GetFileNameWithoutExtension(sourcePath) + baseNameSuffix;
        string sourceExtension = Path.GetExtension(sourcePath)
            .TrimStart('.')
            .ToLowerInvariant();

        if (sourceExtension == format.Extension())
            baseName += "_converted";

        string candidate = Path.Combine(directory, $"{baseName}.{format.Extension()}");
        int suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(
                directory,
                $"{baseName}_{suffix}.{format.Extension()}");
            suffix++;
        }

        return candidate;
    }
}
