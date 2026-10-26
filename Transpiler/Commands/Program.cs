using Transpiler;

if (args.Length > 0 && args[0] == "inventory")
    return await ApiInventoryCommand.RunAsync(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "discover-mappings")
    return await ApiInventoryCommand.RunAsync(args.Skip(1).ToArray(), discoveryMode: true);

return await QuestTranspilationWorkflow.RunAsync(args);
