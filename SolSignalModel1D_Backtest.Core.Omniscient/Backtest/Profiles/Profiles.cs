using SolSignalModel1D_Backtest.Core.Omniscient.Backtest;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest.Profiles
	{
	/// <summary>
	/// Профиль бэктеста:
	/// - описывает "тему" / набор настроек;
	/// - содержит конкретный BacktestConfig, который можно использовать для прогонов.
	/// </summary>
	public sealed class BacktestProfile
		{
		/// <summary>
		/// Уникальный идентификатор профиля.
		/// Например: "baseline", "aggressive-10x", "ultra-safe".
		/// </summary>
		public string Id { get; init; } = string.Empty;

		/// <summary>
		/// Человекопонятное имя профиля для отображения.
		/// Например: "Baseline", "Risk aware 3x".
		/// </summary>
		public string Name { get; init; } = string.Empty;

		/// <summary>
		/// Необязательное описание профиля (что это за стратегия).
		/// </summary>
		public string? Description { get; init; }

		/// <summary>
		/// Системный ли профиль (создан кодом, а не пользователем).
		/// Например, baseline-профиль считается системным.
		/// </summary>
		public bool IsSystem { get; init; }

		/// <summary>
		/// Конфигурация бэктеста, которая соответствует этому профилю.
		/// </summary>
		public BacktestConfig Config { get; init; } = new BacktestConfig ();

		// Категория профиля: system / user / scratch / ...
		public string Category { get; set; } = "system";

		// Флаг "избранный профиль" (на стороне бэка).
		public bool IsFavorite { get; set; }

		}
	}
