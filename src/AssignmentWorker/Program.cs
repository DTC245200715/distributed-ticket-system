using DistributedTicketSystem.AssignmentWorker;

var builder = Host.CreateApplicationBuilder(args);

// Cấu hình Logging ra Console
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Đăng ký Worker làm Hosted Service
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
