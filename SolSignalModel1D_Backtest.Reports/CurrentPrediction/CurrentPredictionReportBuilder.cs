using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Reports.CurrentPrediction
	{
	/// <summary>
	/// Строит ReportDocument для "текущего прогноза" на основе CurrentPredictionSnapshot.
	/// Здесь только форматирование в key-value и таблицы, без новой математики.
	/// </summary>
	public static class CurrentPredictionReportBuilder
		{
		public static ReportDocument Build ( CurrentPredictionSnapshot snapshot )
			{
			if (snapshot == null)
				throw new ArgumentNullException (
					nameof (snapshot),
					"[current-report] CurrentPredictionSnapshot == null — нарушен инвариант построения отчёта");

			var doc = new ReportDocument
				{
				Id = $"current-prediction-{snapshot.PredictionDateUtc:yyyyMMdd}-{snapshot.GeneratedAtUtc:HHmmss}",
				Kind = "current_prediction",
				Title = $"Текущий прогноз ({TradingSymbols.SolUsdtDisplay})",
				GeneratedAtUtc = snapshot.GeneratedAtUtc
				};

			var info = new KeyValueSection
				{
				Title = "Общие параметры прогноза"
				};

			info.Items.Add (new KeyValueItem
				{
				Key = "Время генерации отчёта (UTC)",
				Value = FormatDateUtc (snapshot.GeneratedAtUtc)
				});

			info.Items.Add (new KeyValueItem
				{
				Key = "Дата прогноза (UTC)",
				Value = FormatDateUtc (snapshot.PredictionDateUtc)
				});

			info.Items.Add (new KeyValueItem
				{
				Key = "Основная модель (Daily)",
				Value = BuildMainDirectionLabel (snapshot)
				});

			if (!string.IsNullOrWhiteSpace (snapshot.MicroDisplay))
				{
				info.Items.Add (new KeyValueItem
					{
					Key = "Микро-модель (1m)",
					Value = snapshot.MicroDisplay
					});
				}

			info.Items.Add (new KeyValueItem
				{
				Key = "Режим рынка",
				Value = snapshot.RegimeDown
					? "Рынок в фазе снижения"
					: "Рынок в нормальном режиме"
				});

			info.Items.Add (new KeyValueItem
				{
				Key = "Вероятность срабатывания стоп-лосса",
				Value = $"{snapshot.SlProb:0.0} %"
				});

			info.Items.Add (new KeyValueItem
				{
				Key = "Сигнал SL-модели",
				Value = FormatSlDecision (snapshot.SlHighDecision)
				});

			info.Items.Add (new KeyValueItem
				{
				Key = $"Текущая цена {TradingSymbols.SolUsdtDisplay}",
				Value = snapshot.Entry.ToString ("0.0000")
				});

			info.Items.Add (new KeyValueItem
				{
				Key = "Минимальный осмысленный ход цены",
				Value = $"{snapshot.MinMove:0.0000} ({snapshot.MinMove * 100:0.0} %)"
				});

			if (!string.IsNullOrWhiteSpace (snapshot.Reason))
				{
				info.Items.Add (new KeyValueItem
					{
					Key = "Комментарий модели",
					Value = snapshot.Reason
					});
				}

			doc.KeyValueSections.Add (info);

			if (snapshot.Forward24h != null)
				{
				var fwd = snapshot.Forward24h;

				var fwdSection = new KeyValueSection
					{
					Title = "Диапазон цены за 24 часа (исторический baseline)"
					};

				fwdSection.Items.Add (new KeyValueItem
					{
					Key = "Максимальная цена за 24 часа",
					Value = fwd.MaxHigh.ToString ("0.0000")
					});
				fwdSection.Items.Add (new KeyValueItem
					{
					Key = "Минимальная цена за 24 часа",
					Value = fwd.MinLow.ToString ("0.0000")
					});
				fwdSection.Items.Add (new KeyValueItem
					{
					Key = "Цена закрытия через 24 часа",
					Value = fwd.Close.ToString ("0.0000")
					});

				doc.KeyValueSections.Add (fwdSection);
				}

			// Таблица с top-факторами, которые повлияли на прогноз
			if (snapshot.ExplanationItems.Count > 0)
				{
				var explain = new TableSection
					{
					Title = "Почему модель дала такой прогноз (top факторов)"
					};

				explain.Columns.AddRange (new[]
					{
					"Тип",
					"Имя",
					"Описание",
					"Значение",
					"Ранг"
					});

				var ordered = snapshot.ExplanationItems
					.OrderBy (e => e.Rank == 0 ? int.MaxValue : e.Rank)
					.ThenBy (e => e.Name);

				foreach (var item in ordered)
					{
					explain.Rows.Add (new List<string>
						{
						item.Kind,
						item.Name,
						item.Description,
						item.Value.HasValue
							? item.Value.Value.ToString ("0.####")
							: string.Empty,
						item.Rank > 0
							? item.Rank.ToString (CultureInfo.InvariantCulture)
							: string.Empty
						});
					}

				doc.TableSections.Add (explain);
				}

			var table = new TableSection
				{
				Title = "Политики плеча (BASE vs ANTI-D)"
				};

			table.Columns.AddRange (new[]
			{
				"Политика",
				"Ветка",
				"Рискованный день",
				"Есть направление",
				"Пропущено",
				"Направление",
				"Плечо",
				"Цена входа",
				"SL, %",
				"TP, %",
				"Цена SL",
				"Цена TP",
				"Размер позиции, $",
				"Размер позиции, qty",
				"Цена ликвидации",
				"Дистанция до ликвидации, %"
			});

			foreach (var row in snapshot.PolicyRows)
				{
				string F ( double? v, string fmt ) => v.HasValue ? v.Value.ToString (fmt) : "-";

				table.Rows.Add (new List<string>
				{
					row.PolicyName,
					row.Branch,
					row.IsRiskDay.ToString(),
					row.HasDirection.ToString(),
					row.Skipped.ToString(),
					row.Direction,
					row.Leverage.ToString("0.##"),
					row.Entry.ToString("0.0000"),
					row.SlPct.HasValue ? row.SlPct.Value.ToString("0.0") : "-",
					row.TpPct.HasValue ? row.TpPct.Value.ToString("0.0") : "-",
					F(row.SlPrice, "0.0000"),
					F(row.TpPrice, "0.0000"),
					F(row.PositionUsd, "0.00"),
					F(row.PositionQty, "0.000"),
					F(row.LiqPrice, "0.0000"),
					F(row.LiqDistPct, "0.0")
				});
				}

			doc.TableSections.Add (table);

			return doc;
			}

		private static string FormatDateUtc ( DateTime dtUtc )
			{
			var utc = dtUtc.Kind == DateTimeKind.Utc ? dtUtc : dtUtc.ToUniversalTime ();
			return utc.ToString ("yyyy-MM-dd HH:mm 'UTC'");
			}

		private static string BuildMainDirectionLabel ( CurrentPredictionSnapshot snapshot )
			{
			var rawLabel = snapshot.PredLabel;
			var raw = rawLabel.ToString (CultureInfo.InvariantCulture);
			var rawTrim = raw.Trim ();
			var rawLower = rawTrim.ToLowerInvariant ();

			bool? baseFlat = null;
			bool? baseUp = null;

			if (rawLabel == 0)
				{
				baseFlat = true;
				}
			else if (rawLabel > 0)
				{
				baseFlat = false;
				baseUp = true;
				}
			else if (rawLabel < 0)
				{
				baseFlat = false;
				baseUp = false;
				}

			if (rawLower.IndexOf ("flat", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("флэт", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("sideways", StringComparison.Ordinal) >= 0)
				{
				baseFlat ??= true;
				}

			if (rawLower.IndexOf ("up", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("long", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("рост", StringComparison.Ordinal) >= 0)
				{
				baseUp ??= true;
				}

			if (rawLower.IndexOf ("down", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("short", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("пад", StringComparison.Ordinal) >= 0)
				{
				baseUp ??= false;
				}

			var micro = snapshot.MicroDisplay?.ToLowerInvariant () ?? string.Empty;
			bool microUp = micro.Contains ("up") || micro.Contains ("рост");
			bool microDown = micro.Contains ("down") || micro.Contains ("пад");

			if (baseFlat == true)
				{
				if (microUp)
					return "Боковик-Рост";
				if (microDown)
					return "Боковик-Падение";
				return "Боковик";
				}

			if (baseUp == true)
				return "Рост";

			if (baseUp == false)
				return "Падение";

			return string.IsNullOrWhiteSpace (rawTrim) ? "нет данных" : rawTrim;
			}

		private static string FormatSlDecision ( object? slDecision )
			{
			if (slDecision == null)
				return "нет данных";

			var raw = slDecision.ToString () ?? string.Empty;
			var lower = raw.Trim ().ToLowerInvariant ();

			if (lower == "true" || lower == "1" || lower == "high")
				return "Высокий риск: стопы стоит усилить";

			if (lower == "false" || lower == "0" || lower == "ok")
				return "Нормальный риск по стопам";

			return raw;
			}
		}
	}
