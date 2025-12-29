using SolSignalModel1D_Backtest.Api.Dto;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest.Profiles;
using SolSignalModel1D_Backtest.Core.Omniscient.Backtest.Services;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl;
using SolSignalModel1D_Backtest.Reports;
using SolSignalModel1D_Backtest.Reports.Backtest.Reports;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Api.Endpoints
	{
	internal static class BacktestEndpoints
		{
		public static IEndpointRouteBuilder MapBacktestEndpoints ( this IEndpointRouteBuilder app )
			{
			// Разбито на небольшие методы, чтобы файл не разрастался.
			MapBacktestSummary (app);
			MapBacktestBaselineSnapshot (app);
			MapBacktestConfigLegacy (app);
			MapProfilesListAndGet (app);
			MapProfilesCreation (app);
			MapProfilesUpdate (app);
			MapBacktestPreview (app);

			return app;
			}

		// === 1. summary по бэктесту (ReportDocument) ===
		private static void MapBacktestSummary ( IEndpointRouteBuilder app )
			{
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
			}

		// === 2. baseline-снимок бэктеста (лёгкий DTO) ===
		private static void MapBacktestBaselineSnapshot ( IEndpointRouteBuilder app )
			{
			app.MapGet ("/api/backtest/baseline", ( BacktestBaselineStorage storage ) =>
			{
				var snapshot = storage.LoadLatest ();
				if (snapshot == null)
					{
					return Results.NotFound (new
						{
						error = "backtest_baseline_not_found",
						message = "Нет сохранённого baseline-среза бэктеста."
						});
					}

				return Results.Ok (snapshot);
			})
			.WithName ("GetBacktestBaseline")
			.Produces<BacktestBaselineSnapshot> (StatusCodes.Status200OK)
			.Produces (StatusCodes.Status404NotFound);
			}

		// === 3. baseline-конфиг бэктеста (legacy для старого UI) ===
		private static void MapBacktestConfigLegacy ( IEndpointRouteBuilder app )
			{
			app.MapGet ("/api/backtest/config", () =>
			{
				var cfg = BacktestConfigFactory.CreateBaseline ();

				var dto = new BacktestConfigDto
					{
					DailyStopPct = cfg.DailyStopPct,
					DailyTpPct = cfg.DailyTpPct
					};

				if (cfg.Policies != null)
					{
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
					}

				return Results.Ok (dto);
			});
			}

		// === 4. список профилей + профиль по id ===
		private static void MapProfilesListAndGet ( IEndpointRouteBuilder app )
			{
			// GET /api/backtest/profiles
			// Возвращаем профили сразу с полным Config,
			// чтобы фронт мог делать draftConfig = clone(profile.config).
			app.MapGet ("/api/backtest/profiles", async (
				IBacktestProfileRepository repository,
				CancellationToken cancellationToken ) =>
			{
				var profiles = await repository.GetAllAsync (cancellationToken);
				var dtos = profiles.Select (p => MapProfileToDto (p, includeConfig: true));
				return Results.Ok (dtos);
			})
			.WithName ("GetBacktestProfiles")
			.Produces<IEnumerable<BacktestProfileDto>> (StatusCodes.Status200OK);

			// GET /api/backtest/profiles/{id}
			app.MapGet ("/api/backtest/profiles/{id}", async (
				string id,
				IBacktestProfileRepository repository,
				CancellationToken cancellationToken ) =>
			{
				if (string.IsNullOrWhiteSpace (id))
					{
					return Results.BadRequest (new
						{
						error = "profile_id_required",
						message = "Не указан идентификатор профиля."
						});
					}

				var profile = await repository.GetByIdAsync (id, cancellationToken);
				if (profile == null)
					{
					return Results.NotFound (new
						{
						error = "profile_not_found",
						message = $"Профиль бэктеста '{id}' не найден."
						});
					}

				var dto = MapProfileToDto (profile, includeConfig: true);
				return Results.Ok (dto);
			})
			.WithName ("GetBacktestProfileById")
			.Produces<BacktestProfileDto> (StatusCodes.Status200OK)
			.Produces (StatusCodes.Status404NotFound)
			.Produces (StatusCodes.Status400BadRequest);
			}

		// === 5. создание профиля (POST /api/backtest/profiles) ===
		private static void MapProfilesCreation ( IEndpointRouteBuilder app )
			{
			app.MapPost ("/api/backtest/profiles", async (
				BacktestProfileCreateDto dto,
				IBacktestProfileRepository repository,
				CancellationToken cancellationToken ) =>
			{
				if (dto == null)
					{
					return Results.BadRequest (new
						{
						error = "profile_create_body_required",
						message = "Тело запроса для создания профиля обязательно."
						});
					}

				var name = dto.Name?.Trim ();
				if (string.IsNullOrWhiteSpace (name))
					{
					return Results.BadRequest (new
						{
						error = "profile_name_required",
						message = "Имя профиля не может быть пустым."
						});
					}

				if (dto.Config == null)
					{
					return Results.BadRequest (new
						{
						error = "profile_config_required",
						message = "Конфиг бэктеста (config) обязателен."
						});
					}

				BacktestConfig config;
				try
					{
					config = MapConfigDtoToDomain (dto.Config);
					}
				catch (ArgumentException ex)
					{
					return Results.BadRequest (new
						{
						error = "invalid_profile_config",
						message = ex.Message
						});
					}

				var category = string.IsNullOrWhiteSpace (dto.Category)
					? "user"
					: dto.Category!.Trim ();

				var profile = new BacktestProfile
					{
					Id = $"user-{Guid.NewGuid ():N}",
					Name = name,
					Description = dto.Description,
					IsSystem = false,
					Category = category,
					IsFavorite = dto.IsFavorite ?? false,
					Config = config
					};

				var saved = await repository.SaveAsync (profile, cancellationToken);
				var resultDto = MapProfileToDto (saved, includeConfig: true);
				return Results.Ok (resultDto);
			})
			.WithName ("CreateBacktestProfile")
			.Produces<BacktestProfileDto> (StatusCodes.Status200OK)
			.Produces (StatusCodes.Status400BadRequest);
			}

		// === 6. частичное обновление профиля (PATCH /api/backtest/profiles/{id}) ===
		private static void MapProfilesUpdate ( IEndpointRouteBuilder app )
			{
			app.MapPatch ("/api/backtest/profiles/{id}", async (
				string id,
				BacktestProfileUpdateDto dto,
				IBacktestProfileRepository repository,
				CancellationToken cancellationToken ) =>
			{
				if (string.IsNullOrWhiteSpace (id))
					{
					return Results.BadRequest (new
						{
						error = "profile_id_required",
						message = "Не указан идентификатор профиля."
						});
					}

				if (dto == null)
					{
					return Results.BadRequest (new
						{
						error = "profile_update_body_required",
						message = "Тело запроса для обновления профиля обязательно."
						});
					}

				var profile = await repository.GetByIdAsync (id, cancellationToken);
				if (profile == null)
					{
					return Results.NotFound (new
						{
						error = "profile_not_found",
						message = $"Профиль бэктеста '{id}' не найден."
						});
					}

				// Собираем новые значения без мутации init-only свойств.
				var newName = profile.Name;
				if (dto.Name != null)
					{
					var trimmed = dto.Name.Trim ();
					if (string.IsNullOrWhiteSpace (trimmed))
						{
						return Results.BadRequest (new
							{
							error = "profile_name_empty",
							message = "Имя профиля не может быть пустой строкой."
							});
						}

					newName = trimmed;
					}

				var newCategory = profile.Category;
				if (dto.Category != null)
					{
					var trimmedCat = dto.Category.Trim ();
					if (!string.IsNullOrWhiteSpace (trimmedCat))
						{
						newCategory = trimmedCat;
						}
					}

				var newIsFavorite = profile.IsFavorite;
				if (dto.IsFavorite.HasValue)
					{
					newIsFavorite = dto.IsFavorite.Value;
					}

				// Создаём новый экземпляр BacktestProfile, чтобы соблюсти init-only.
				var updatedProfile = new BacktestProfile
					{
					Id = profile.Id,
					Name = newName,
					Description = profile.Description,
					IsSystem = profile.IsSystem,
					Category = newCategory,
					IsFavorite = newIsFavorite,
					Config = profile.Config
					};

				var saved = await repository.SaveAsync (updatedProfile, cancellationToken);
				var resultDto = MapProfileToDto (saved, includeConfig: true);

				return Results.Ok (resultDto);
			})
			.WithName ("UpdateBacktestProfile")
			.Produces<BacktestProfileDto> (StatusCodes.Status200OK)
			.Produces (StatusCodes.Status400BadRequest)
			.Produces (StatusCodes.Status404NotFound);
			}

		// === 7. one-shot preview бэктеста по BacktestConfig ===
		private static void MapBacktestPreview ( IEndpointRouteBuilder app )
			{
			app.MapPost ("/api/backtest/preview", async (
				BacktestPreviewRequestDto request,
				IBacktestDataProvider dataProvider,
				BacktestPreviewService previewService,
				CancellationToken cancellationToken ) =>
			{
				BacktestConfig config;

				// В превью допустим пустой body/config: используем baseline.
				if (request?.Config == null)
					{
					config = BacktestConfigFactory.CreateBaseline ();
					}
				else
					{
					try
						{
						config = MapConfigDtoToDomain (request.Config);
						}
					catch (ArgumentException ex)
						{
						return Results.BadRequest (new
							{
							error = "invalid_preview_config",
							message = ex.Message
							});
						}
					}

				// Фильтрация по SelectedPolicies (если передали список имен).
				if (request?.SelectedPolicies != null && request.SelectedPolicies.Count > 0)
					{
					var selected = new HashSet<string> (
						request.SelectedPolicies
							.Where (x => !string.IsNullOrWhiteSpace (x))
							.Select (x => x.Trim ()),
						StringComparer.OrdinalIgnoreCase);

					if (selected.Count == 0)
						{
						return Results.BadRequest (new
							{
							error = "selected_policies_empty",
							message = "SelectedPolicies содержит только пустые значения."
							});
						}

					var srcPolicies = config.Policies;
					if (srcPolicies == null)
						{
						// Это не “нормальная ситуация”: конфиг невалиден.
						return Results.Problem (
							title: "invalid_backtest_config",
							detail: "BacktestConfig.Policies is null.",
							statusCode: StatusCodes.Status500InternalServerError);
						}

					var filtered = srcPolicies
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

					// Превью должно быть детерминированным и не зависеть от мутаций исходного конфига.
					// Поэтому создаём новый BacktestConfig вместо Clear()/Add().
					config = new BacktestConfig
						{
						DailyStopPct = config.DailyStopPct,
						DailyTpPct = config.DailyTpPct,
						Policies = filtered
						};
					}

				BacktestDataSnapshot snapshot;
				try
					{
					snapshot = await dataProvider.LoadAsync (cancellationToken);
					}
				catch (Exception ex)
					{
					return Results.Problem (
						title: "data_load_failed",
						detail: ex.Message,
						statusCode: StatusCodes.Status500InternalServerError);
					}

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
					return Results.BadRequest (new
						{
						error = "invalid_preview_config",
						message = ex.Message
						});
					}
				catch (Exception ex)
					{
					return Results.Problem (
						title: "preview_failed",
						detail: ex.Message,
						statusCode: StatusCodes.Status500InternalServerError);
					}

				var report = BacktestSummaryReportBuilder.Build (summary);
				if (report == null)
					{
					return Results.Problem (
						title: "preview_report_not_built",
						detail: "Отчёт превью-бэктеста не был построен (нет политик или данных).",
						statusCode: StatusCodes.Status500InternalServerError);
					}

				return Results.Ok (report);
			});
			}

		// === Вспомогательные мапперы ===

		private static BacktestConfig MapConfigDtoToDomain ( BacktestConfigDto dto )
			{
			if (dto == null) throw new ArgumentNullException (nameof (dto));

			var cfg = new BacktestConfig
				{
				DailyStopPct = dto.DailyStopPct,
				DailyTpPct = dto.DailyTpPct,
				Policies = new List<PolicyConfig> ()
				};

			if (dto.Policies == null)
				return cfg;

			for (int i = 0; i < dto.Policies.Count; i++)
				{
				var p = dto.Policies[i];
				if (p == null)
					throw new ArgumentException ($"Policies[{i}] is null.", nameof (dto));

				var name = p.Name?.Trim ();
				if (string.IsNullOrWhiteSpace (name))
					throw new ArgumentException ($"Policies[{i}].Name is required.", nameof (dto));

				var policyType = p.PolicyType?.Trim ();
				if (string.IsNullOrWhiteSpace (policyType))
					throw new ArgumentException ($"Policy '{name}': PolicyType is required.", nameof (dto));

				var marginModeText = p.MarginMode?.Trim ();
				if (string.IsNullOrWhiteSpace (marginModeText))
					throw new ArgumentException ($"Policy '{name}': MarginMode is required.", nameof (dto));

				if (!Enum.TryParse<MarginMode> (marginModeText, ignoreCase: true, out var mm))
					throw new ArgumentException ($"Policy '{name}': invalid MarginMode '{marginModeText}'.", nameof (dto));

				cfg.Policies.Add (new PolicyConfig
					{
					Name = name,
					PolicyType = policyType,
					Leverage = p.Leverage,
					MarginMode = mm
					});
				}

			return cfg;
			}

		private static BacktestProfileDto MapProfileToDto ( BacktestProfile profile, bool includeConfig )
			{
			BacktestConfigDto? configDto = null;

			if (includeConfig && profile.Config != null)
				{
				var cfg = profile.Config;

				configDto = new BacktestConfigDto
					{
					DailyStopPct = cfg.DailyStopPct,
					DailyTpPct = cfg.DailyTpPct
					};

				if (cfg.Policies != null)
					{
					foreach (var p in cfg.Policies)
						{
						configDto.Policies.Add (new PolicyConfigDto
							{
							Name = p.Name,
							PolicyType = p.PolicyType,
							Leverage = p.Leverage,
							MarginMode = p.MarginMode.ToString ()
							});
						}
					}
				}

			return new BacktestProfileDto
				{
				Id = profile.Id,
				Name = profile.Name,
				Description = profile.Description,
				IsSystem = profile.IsSystem,
				Category = profile.Category,
				IsFavorite = profile.IsFavorite,
				Config = configDto
				};
			}
		}
	}
