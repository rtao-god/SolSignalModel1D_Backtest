using SolSignalModel1D_Backtest.Reports;

namespace SolSignalModel1D_Backtest.Api.Endpoints
	{
	internal static class PfiEndpoints
		{
		public static IEndpointRouteBuilder MapPfiEndpoints ( this IEndpointRouteBuilder app )
			{
			// GET /api/ml/pfi/per-model
			app.MapGet ("/api/ml/pfi/per-model", ( ReportStorage storage ) =>
			{
				var report = storage.LoadLatestByKind ("pfi_per_model");
				if (report == null)
					{
					return Results.NotFound (new
						{
						error = "pfi_report_not_found",
						message = "Нет сохранённого PFI-отчёта по моделям."
						});
					}

				return Results.Ok (report);
			});

			return app;
			}
		}
	}
