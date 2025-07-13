var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Leaderboard>("leaderboard");

builder.Build().Run();
