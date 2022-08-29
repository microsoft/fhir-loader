namespace Applied.FhirLoader
{
    public interface IFileHandler
    {
        IEnumerable<(string bundle, int count)> ConvertToBundles();
    }
}
