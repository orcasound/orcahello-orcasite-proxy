// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using OrcaHelloProxy;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<PeriodicTasks>(); // Register your background service
var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
