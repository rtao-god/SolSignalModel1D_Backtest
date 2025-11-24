using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SolSignalModel1D_Backtest.Api.Dto;
using SolSignalModel1D_Backtest.Api.Services;
using SolSignalModel1D_Backtest.Core.Analytics.Backtest;
using SolSignalModel1D_Backtest.Core.Analytics.Reports;
using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Backtest.Services;
using SolSignalModel1D_Backtest.Core.Utils.Pnl;
using SolSignalModel1D_Backtest.Reports;
using SolSignalModel1D_Backtest.Reports.Model;

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

// превью-бэктест (PnL-движок без консольного вывода)
builder.Services.AddSingleton<BacktestPreviewService> ();

// провайдер данных для бэктеста/превью (каркас, реализацию нужно дописать)
builder.Services.AddSingleton<IBacktestDataProvider, BacktestDataProvider> ();

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

// GET /api/backtest/baseline
app.MapGet ("/api/backtest/baseline", ( ReportStorage storage ) =>
{
	var snapshot = storage.LoadLatest<BacktestBaselineSnapshot> ("backtest_baseline");
	if (snapshot == null)
		{
		return Results.NotFound (new
			{
			error = "baseline_not_found",
			message = "Нет сохранённого baseline-среза бэктеста."
			});
		}

	return Results.Ok (snapshot);
})
.WithName ("GetBacktestBaseline")
.Produces<BacktestBaselineSnapshot> (StatusCodes.Status200OK)
.Produces (StatusCodes.Status404NotFound);

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
	BacktestPreviewRequestDto request,
	IBacktestDataProvider dataProvider,
	BacktestPreviewService previewService,
	CancellationToken cancellationToken ) =>
{
	// 1) Собираем BacktestConfig: либо baseline, либо на базе DTO.
	BacktestConfig config;
	if (request.Config == null)
		{
		config = BacktestConfigFactory.CreateBaseline ();
		}
	else
		{
		// Маппинг DTO → BacktestConfig (минимальный, без валидации тонких кейсов).
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

	// 3) Загружаем данные для бэктеста через общий провайдер.
	BacktestDataSnapshot snapshot;
	try
		{
		snapshot = await dataProvider.LoadAsync (cancellationToken);
		}
	catch (Exception ex)
		{
		// Явно отделяем проблемы загрузки данных.
		return Results.Problem (
			title: "data_load_failed",
			detail: ex.Message,
			statusCode: StatusCodes.Status500InternalServerError);
		}

	// 4) Считаем превью-бэктест через BacktestPreviewService (PnL без консольного вывода).
	BacktestSummary summary;
	try
		{
		summary = previewService.RunPreview (
			snapshot.Mornings,
			snapshot.Records,
			snapshot.Candles1m,
			config);
		}
	catch (ArgumentException ex)
		{
		// Конфиг/данные явно некорректны.
		return Results.BadRequest (new
			{
			error = "invalid_preview_config",
			message = ex.Message
			});
		}
	catch (Exception ex)
		{
		// Внутренняя ошибка при расчёте превью.
		return Results.Problem (
			title: "preview_failed",
			detail: ex.Message,
			statusCode: StatusCodes.Status500InternalServerError);
		}

	// 5) Строим ReportDocument в том же формате, что baseline backtest_summary.
	var report = BacktestSummaryReportBuilder.Build (summary);
	if (report == null)
		{
		return Results.Problem (
			title: "preview_report_not_built",
			detail: "Отчёт превью-бэктеста не был построен (нет политик или данных).",
			statusCode: StatusCodes.Status500InternalServerError);
		}

	// ВАЖНО: здесь мы не сохраняем отчёт в ReportStorage, а просто отдаём его фронту,
	// чтобы baseline и preview были одинакового формата по JSON.
	return Results.Ok (report);
});

app.Run ();
