using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--canonical", StringComparison.Ordinal))
        {
            string directory = args.Length > 1 ? args[1] : Path.Combine("artifacts", "canonical");
            return CanonicalOutput.Write(directory);
        }

        if (args.Length > 0 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            Console.WriteLine("usage: KeelMatrix.JsonDrift.RuleMatrix [--matrix [--verbose] | --canonical <directory>]");
            Console.WriteLine();
            Console.WriteLine("  --matrix             run the compatibility rule matrix and print the report (default)");
            Console.WriteLine("  --matrix --verbose   include the change description for every check");
            Console.WriteLine("  --canonical <dir>    write the canonical contract document for the representative root contract");
            return 0;
        }

        bool verbose = args.Any(static argument => string.Equals(argument, "--verbose", StringComparison.Ordinal));
        return MatrixReport.Run(Console.Out, verbose);
    }
}
