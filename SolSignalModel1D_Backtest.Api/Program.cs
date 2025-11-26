using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SolSignalModel1D_Backtest.Api.Endpoints;
using SolSignalModel1D_Backtest.Api.Services;
using SolSignalModel1D_Backtest.Core.Backtest.Profiles;
using SolSignalModel1D_Backtest.Core.Backtest.Services;
using SolSignalModel1D_Backtest.Reports;
using SolSignalModel1D_Backtest.Reports.Backtest.Reports;

var builder = WebApplication.CreateBuilder (args);

builder.Services.AddRouting ();

// CORS: в dev пускаем всех
builder.Services.AddCors (options =>
{
	options.AddDefaultPolicy (policy =>
	{
		policy
			.AllowAnyOrigin ()
			.AllowAnyHeader ()
			.AllowAnyMethod ();
	});
});

builder.Services.AddEndpointsApiExplorer ();
builder.Services.AddSwaggerGen ();

// файловое хранилище отчётов
builder.Services.AddSingleton<ReportStorage> ();

// лёгкое хранилище baseline-снапшота бэктеста
builder.Services.AddSingleton<BacktestBaselineStorage> ();

// превью-бэктест (PnL-движок без консольного вывода)
builder.Services.AddSingleton<BacktestPreviewService> ();

// провайдер данных для бэктеста/превью
builder.Services.AddSingleton<IBacktestDataProvider, BacktestDataProvider> ();

// репозиторий профилей бэктеста (JSON)
builder.Services.AddSingleton<IBacktestProfileRepository, JsonBacktestProfileRepository> ();

var app = builder.Build ();

if (app.Environment.IsDevelopment ())
	{
	app.UseSwagger ();
	app.UseSwaggerUI (c =>
	{
		c.SwaggerEndpoint ("/swagger/v1/swagger.json", "SolSignalModel1D API v1");
	});
	}

app.UseRouting ();
app.UseCors ();

// Группы эндпоинтов
app.MapCurrentPredictionEndpoints ();
app.MapBacktestEndpoints ();
app.MapPfiEndpoints ();

app.Run ();
