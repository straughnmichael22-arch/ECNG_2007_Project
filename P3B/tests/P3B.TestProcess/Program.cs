// Harmless helper process used only by ProcessRunnerTests. It never touches files, the
// registry or the network: it echoes arguments and environment variables, writes to stderr, sleeps and returns exit codes.
using System.Globalization;
using System.Text.Json;

switch (args.FirstOrDefault())
{
    case "echo-args":
        Console.Out.Write(JsonSerializer.Serialize(args.Skip(1).ToArray()));
        return 0;

    case "fail":
        Console.Out.Write("TEST standard output");
        Console.Error.Write(args.ElementAtOrDefault(1) ?? "TEST standard error");
        return int.Parse(args.ElementAtOrDefault(2) ?? "3", CultureInfo.InvariantCulture);

    case "print":
        Console.Out.WriteLine(args.ElementAtOrDefault(1) ?? string.Empty);
        return 0;

    case "sleep":
        Thread.Sleep(int.Parse(args[1], CultureInfo.InvariantCulture));
        return 0;

    case "print-env":
        Console.Out.Write(Environment.GetEnvironmentVariable(args[1]) ?? "(unset)");
        return 0;

    case "read-stdin":
        // Returns immediately because the runner closes standard input.
        Console.Out.Write(Console.In.ReadToEnd().Length.ToString(CultureInfo.InvariantCulture));
        return 0;

    default:
        Console.Error.Write("Unknown mode.");
        return 64;
}
