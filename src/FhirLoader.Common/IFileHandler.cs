namespace FhirLoader.Common
{
    public interface IFileHandler
    {
        IEnumerable<(string bundle, int count)> ConvertToBundles();
    }
}
