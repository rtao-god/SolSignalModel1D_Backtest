using System;
using System.Linq;
using SolSignalModel1D_Backtest.Reports;

namespace SolSignalModel1D_Backtest.Api.Endpoints
	{
	internal static class CurrentPredictionEndpoints
		{
		public static IEndpointRouteBuilder MapCurrentPredictionEndpoints ( this IEndpointRouteBuilder app )
			{
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

			// GET /api/current-prediction/dates
			// Возвращает список дат, за которые есть current_prediction-отчёты.
			// Опциональный параметр days ограничивает окно истории.
			app.MapGet ("/api/current-prediction/dates", ( ReportStorage storage, int? days ) =>
			{
				var index = storage.ListCurrentPredictionReports ();

				if (index.Count == 0)
					{
					return Results.Ok (Array.Empty<object> ());
					}

				DateTime? cutoff = null;
				if (days.HasValue && days.Value > 0)
					{
					cutoff = DateTime.UtcNow.Causal.DateUtc.AddDays (-days.Value);
					}

				var items = index
					.Where (x => !cutoff.HasValue || x.PredictionDateUtc.Causal.DateUtc >= cutoff.Value)
					.Select (x => new
						{
						id = x.Id,
						predictionDateUtc = x.PredictionDateUtc
						});

				return Results.Ok (items);
			});

			// GET /api/current-prediction/by-date?dateUtc=YYYY-MM-DD
			// Возвращает отчёт по текущему прогнозу за заданную дату (UTC).
			app.MapGet ("/api/current-prediction/by-date", ( ReportStorage storage, DateTime dateUtc ) =>
			{
				var report = storage.LoadCurrentPredictionByDate (dateUtc);
				if (report == null)
					{
					return Results.NotFound (new
						{
						error = "snapshot_not_found",
						message = $"Нет сохранённого отчёта по текущему прогнозу за дату {dateUtc:yyyy-MM-dd} (UTC)."
						});
					}

				return Results.Ok (report);
			});

			// GET /api/ping
			app.MapGet ("/api/ping", () => Results.Ok ("ok"));

			return app;
			}
		}
	}
