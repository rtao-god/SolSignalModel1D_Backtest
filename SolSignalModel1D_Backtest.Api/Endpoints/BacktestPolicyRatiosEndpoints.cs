using Microsoft.AspNetCore.Routing;
using SolSignalModel1D_Backtest.Reports;
using SolSignalModel1D_Backtest.Reports.Backtest.PolicyRatios;

namespace SolSignalModel1D_Backtest.Api.Endpoints
	{
	internal static class BacktestPolicyRatiosEndpoints
		{
		public static IEndpointRouteBuilder MapBacktestPolicyRatiosEndpoints ( this IEndpointRouteBuilder app )
			{
			// GET /api/backtest/policy-ratios/{profileId}
			// profileId:
			//   - "baseline" для baseline-профиля;
			//   - id пользовательского профиля (на будущее).
			app.MapGet ("/api/backtest/policy-ratios/{profileId}", ( string profileId, ReportStorage storage ) =>
			{
				if (string.IsNullOrWhiteSpace (profileId))
					{
					profileId = "baseline";
					}

				var report = storage.LoadByKindAndId<PolicyRatiosReportDto> ("policy_ratios", profileId);
				if (report == null)
					{
					return Results.NotFound (new
						{
						error = "policy_ratios_not_found",
						message = $"Отчёт policy_ratios для профиля '{profileId}' не найден. Возможно, бэктест ещё не запускался."
						});
					}

				return Results.Ok (report);
			});

			return app;
			}
		}
	}
