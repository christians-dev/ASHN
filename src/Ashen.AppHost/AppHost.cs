var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres").WithDataVolume();
var database = postgres.AddDatabase("ashen");

// Local catch-all inbox: the store's emails appear in the Mailpit web UI, nothing is really sent.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.27")
    .WithHttpEndpoint(targetPort: 8025, name: "ui")
    .WithEndpoint(targetPort: 1025, name: "smtp", scheme: "tcp");
var smtp = mailpit.GetEndpoint("smtp");

var api = builder.AddProject<Projects.Ashen_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("Email__Smtp__Host", smtp.Property(EndpointProperty.Host))
    .WithEnvironment("Email__Smtp__Port", smtp.Property(EndpointProperty.Port))
    .WaitFor(mailpit);

// Fixed port: Ashen:Store:BaseUrl (links in emails) points at http://localhost:5173 in Development.
builder.AddViteApp("web", "../../web")
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
