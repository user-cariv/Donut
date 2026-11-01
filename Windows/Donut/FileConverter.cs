using System.IO;
using System.Runtime.InteropServices;
using ImageMagick;
using LibHeifSharp;

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

        if (format is OutputFormat.Jpeg
            or OutputFormat.Png
            or OutputFormat.Webp
            or OutputFormat.Tiff
            or OutputFormat.Pdf)
            return ConvertWithMagick(sourcePath, format, destination);

        if (format == OutputFormat.Heic)
            return ConvertWithHeic(sourcePath, destination);

        throw new NotSupportedException($"The {format} output format is not supported.");
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
        if (format == OutputFormat.Jpeg)
            image.Alpha(AlphaAction.Remove);

        image.Format = format switch
        {
            OutputFormat.Jpeg => MagickFormat.Jpeg,
            OutputFormat.Png => MagickFormat.Png,
            OutputFormat.Webp => MagickFormat.WebP,
            OutputFormat.Tiff => MagickFormat.Tiff,
            OutputFormat.Pdf => MagickFormat.Pdf,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        image.Write(destination);
        return destination;
    }

    private static string ConvertWithHeic(string sourcePath, string destination)
    {
        using var image = new MagickImage(sourcePath)
        {
            BackgroundColor = MagickColors.White
        };
        image.Format = MagickFormat.Rgb;
        byte[] pixels = image.ToByteArray();
        int width = checked((int)image.Width);
        int height = checked((int)image.Height);

        if (!LibHeifInfo.HaveEncoder(HeifCompressionFormat.Hevc))
            throw new NotSupportedException("HEIC encoding is unavailable because no HEVC encoder is installed.");

        using var context = new HeifContext();
        var descriptors = context.GetEncoderDescriptors(HeifCompressionFormat.Hevc);
        if (descriptors.Count == 0)
            throw new NotSupportedException("HEIC encoding is unavailable because no HEVC encoder is installed.");

        using var heifImage = new HeifImage(
            width,
            height,
            HeifColorspace.Rgb,
            HeifChroma.InterleavedRgb24);
        heifImage.AddPlane(HeifChannel.Interleaved, width, height, 8);
        HeifPlaneData plane = heifImage.GetPlane(HeifChannel.Interleaved);
        int rowBytes = width * 3;
        for (int row = 0; row < height; row++)
        {
            Marshal.Copy(
                pixels,
                row * rowBytes,
                IntPtr.Add(plane.Scan0, row * plane.Stride),
                rowBytes);
        }

        using var encoder = context.GetEncoder(descriptors[0]);
        encoder.SetLossyQuality(90);
        context.EncodeImage(heifImage, encoder, new HeifEncodingOptions());
        context.WriteToFile(destination);
        return destination;
    }

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
