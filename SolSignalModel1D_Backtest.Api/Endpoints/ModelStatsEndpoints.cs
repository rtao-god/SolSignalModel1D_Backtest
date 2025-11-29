using SolSignalModel1D_Backtest.Reports;

namespace SolSignalModel1D_Backtest.Api.Endpoints
	{
	/// <summary>
	/// Эндпоинты для отдачи отчётов по статистике ML-моделей.
	/// Работают поверх ReportStorage и kind = "backtest_model_stats".
	/// </summary>
	internal static class ModelStatsEndpoints
		{
		public static IEndpointRouteBuilder MapModelStatsEndpoints ( this IEndpointRouteBuilder app )
			{
			// GET /api/ml/stats/per-model
			app.MapGet ("/api/ml/stats/per-model", ( ReportStorage storage ) =>
			{
				// Берём последний отчёт по kind = "backtest_model_stats"
				var report = storage.LoadLatestBacktestModelStats ();
				if (report == null)
					{
					return Results.NotFound (new
						{
						error = "backtest_model_stats_not_found",
						message = "Нет сохранённого отчёта по статистике моделей бэктеста."
						});
					}

				return Results.Ok (report);
			});

			return app;
			}
		}
	}
