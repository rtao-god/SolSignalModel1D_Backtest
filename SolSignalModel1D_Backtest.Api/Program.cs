
using SolSignalModel1D_Backtest.Api.Dto;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using SolSignalModel1D_Backtest.Reports;

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
builder.Services.AddSwaggerGen (c =>
{
	c.SwaggerDoc ("v1", new OpenApiInfo
		{
		Title = "SolSignalModel1D API",
		Version = "v1",
		Description = "REST API для чтения отчётов (current prediction, backtests и т.д.)."
		});
});

// файловое хранилище отчётов
builder.Services.AddSingleton<ReportStorage> ();

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

// GET /api/current-prediction
app.MapGet ("/api/current-prediction", ( ReportStorage storage ) =>
{
	var report = storage.LoadLatestCurrentPrediction ();
	if (report == null)
		{
		return Results.NotFound (new
			{
			error = "snapshot_not_found",
			message = "Нет сохранённого отчёта по текущему прогнозу."
			});
		}

	return Results.Ok (report);
});

// GET /api/backtest/summary
app.MapGet ("/api/backtest/summary", ( ReportStorage storage ) =>
{
	var summary = storage.LoadLatestBacktestSummary ();
	if (summary == null)
		{
		return Results.NotFound (new
			{
			error = "backtest_summary_not_found",
			message = "Нет сохранённого отчёта по бэктесту."
			});
		}

	return Results.Ok (summary);
});

// GET /api/backtest/config (baseline-конфиг)
app.MapGet ("/api/backtest/config", () =>
{
	var cfg = BacktestConfigFactory.CreateBaseline ();

	var dto = new BacktestConfigDto
		{
		DailyStopPct = cfg.DailyStopPct,
		DailyTpPct = cfg.DailyTpPct
		};

	foreach (var p in cfg.Policies)
		{
		dto.Policies.Add (new PolicyConfigDto
			{
			Name = p.Name,
			PolicyType = p.PolicyType,
			Leverage = p.Leverage,
			MarginMode = p.MarginMode.ToString ()
			});
		}

	return Results.Ok (dto);
});

// GET /api/ping
app.MapGet ("/api/ping", () => Results.Ok ("ok"));

// POST /api/backtest/preview (one-shot what-if по BacktestConfig)
app.MapPost ("/api/backtest/preview", async (
	HttpContext httpContext,
	BacktestPreviewRequestDto request ) =>
{
	// 1) Собираем BacktestConfig: либо baseline, либо на базе DTO.
	BacktestConfig config;
	if (request.Config == null)
		{
		config = BacktestConfigFactory.CreateBaseline ();
		}
	else
		{
		// Маппинг DTO ? BacktestConfig (минимальный, без валидации тонких кейсов).
		config = new BacktestConfig
			{
			DailyStopPct = request.Config.DailyStopPct,
			DailyTpPct = request.Config.DailyTpPct,
			Policies = new List<PolicyConfig> ()
			};

		if (request.Config.Policies != null)
			{
			foreach (var p in request.Config.Policies)
				{
				config.Policies.Add (new PolicyConfig
					{
					Name = p.Name ?? string.Empty,
					PolicyType = p.PolicyType ?? string.Empty,
					Leverage = p.Leverage,
					MarginMode = Enum.TryParse<MarginMode> (p.MarginMode, out var mm)
						? mm
						: MarginMode.Cross
					});
				}
			}
		}

	// 2) Опционально фильтруем политики по SelectedPolicies.
	if (request.SelectedPolicies != null && request.SelectedPolicies.Count > 0)
		{
		var selected = new HashSet<string> (request.SelectedPolicies, StringComparer.OrdinalIgnoreCase);

		var filtered = (config.Policies ?? new List<PolicyConfig> ())
			.Where (p => selected.Contains (p.Name))
			.ToList ();

		if (filtered.Count == 0)
			{
			return Results.BadRequest (new
				{
				error = "no_policies_selected",
				message = "После фильтрации список политик пуст. Укажи хотя бы одно корректное имя."
				});
			}

		config.Policies.Clear ();
		foreach (var p in filtered)
			{
			config.Policies.Add (p);
			}
		}

	// 3) На этом шаге нет общего data-орchestrator’а для API.
	// Консоль уже умеет собирать candles/rows/records и вызывать BacktestEngine,
	// но этот пайплайн пока живёт только там. Чтобы не дублировать код и не
	// ломать текущую консоль, здесь честно возвращаем 501.
	//
	// Когда вытащим сбор данных в общий сервис (например, BacktestDataService),
	// сюда останется добавить:
	//   var data = await dataService.LoadAsync(config);
	//   var summary = BacktestEngine.RunBacktest(data.Mornings, data.Records, data.Candles1m, data.Policies, config);
	//   return Results.Ok(MapToDto(summary));
	return Results.StatusCode (StatusCodes.Status501NotImplemented);
});

app.Run ();
