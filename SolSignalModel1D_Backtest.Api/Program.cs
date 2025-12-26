using SolSignalModel1D_Backtest.Api.Endpoints;
using SolSignalModel1D_Backtest.Api.Services;
using SolSignalModel1D_Backtest.Core.Backtest.Profiles;
using SolSignalModel1D_Backtest.Core.Backtest.Services;
using SolSignalModel1D_Backtest.Reports;
using SolSignalModel1D_Backtest.Reports.Backtest.Reports;
using System.Text.Json.Serialization;

namespace SolSignalModel1D_Backtest.Api
	{
	/// <summary>
	/// Здесь настраиваются DI-контейнер, middleware и endpoint-группы.
	/// </summary>
	public class Program
		{
		public static void Main ( string[] args )
			{
			var builder = WebApplication.CreateBuilder (args);

			// Глобальные JSON-настройки для HTTP-ответов
			builder.Services.ConfigureHttpJsonOptions (options =>
			{
				// Разрешаем NaN/Infinity/-Infinity так же, как в ReportStorage,
				// чтобы объекты с такими double полями нормально отдавались наружу.
				options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
			});

			builder.Services.AddRouting ();

			// CORS: в dev-режиме разрешаем любые источники/методы/заголовки
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

			// Файловое хранилище отчётов
			builder.Services.AddSingleton<ReportStorage> ();

			// Хранилище baseline-снапшота бэктеста
			builder.Services.AddSingleton<BacktestBaselineStorage> ();

			// Превью-бэктест (PnL-движок без консольного вывода)
			builder.Services.AddSingleton<BacktestPreviewService> ();

			// Провайдер данных для бэктеста/превью
			builder.Services.AddSingleton<IBacktestDataProvider, BacktestDataProvider> ();

			// Репозиторий профилей бэктеста (JSON)
			builder.Services.AddSingleton<IBacktestProfileRepository, JsonBacktestProfileRepository> ();

			// Построение приложения
			var app = builder.Build ();

			// Swagger только в Dev-окружении
			if (app.Environment.IsDevelopment ())
				{
				app.UseSwagger ();
				app.UseSwaggerUI (c =>
				{
					c.SwaggerEndpoint ("/swagger/v1/swagger.json", "SolSignalModel1D API v1");
				});
				}

			// Подключаем middleware маршрутизации и CORS
			app.UseRouting ();
			app.UseCors ();

			// Регистрация endpoint-групп
			app.MapCurrentPredictionEndpoints ();
			app.MapBacktestEndpoints ();
			app.MapPfiEndpoints ();
			app.MapModelStatsEndpoints ();
			app.MapBacktestPolicyRatiosEndpoints ();

			// Запуск HTTP-хоста (блокирующий вызов)
			app.Run ();
			}
		}
	}
