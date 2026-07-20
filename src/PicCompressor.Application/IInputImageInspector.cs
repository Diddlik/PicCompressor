using PicCompressor.Domain;

namespace PicCompressor.Application;

public interface IInputImageInspector
{
    InputImageInfo Inspect(string path);
}
