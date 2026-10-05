using BoardGame.Kernel;
if (args.Length == 0 || args[0] == "--help") { Console.WriteLine($"Board Game Builder API {EngineVersion.Api}. Use --help or quit."); return 0; }
if (args[0] == "quit") return 0;
Console.Error.WriteLine("Unknown command. Use --help.");
return 2;
