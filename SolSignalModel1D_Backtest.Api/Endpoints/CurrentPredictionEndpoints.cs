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

			// GET /api/ping
			app.MapGet ("/api/ping", () => Results.Ok ("ok"));

			return app;
			}
		}
	}
