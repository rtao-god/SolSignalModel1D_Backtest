using SolSignalModel1D_Backtest.Core.Omniscient.Backtest.Profiles;
using SolSignalModel1D_Backtest.Core.Causal.Infra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Backtest.Services
	{
	/// <summary>
	/// JSON-реализация репозитория профилей бэктеста.
	/// Хранение:
	///   {PathConfig.CacheRoot}/profiles/backtest_profiles.json
	/// Пример:
	///   <repo>/cache/profiles/backtest_profiles.json
	/// </summary>
	public sealed class JsonBacktestProfileRepository : IBacktestProfileRepository
		{
		private readonly string _filePath;
		private readonly JsonSerializerOptions _jsonOptions;
		private readonly object _sync = new ();

		public JsonBacktestProfileRepository ()
			{
			var root = PathConfig.CacheRoot;

			var profilesDir = Path.Combine (root, "profiles");
			Directory.CreateDirectory (profilesDir);

			_filePath = Path.Combine (profilesDir, "backtest_profiles.json");

			_jsonOptions = new JsonSerializerOptions
				{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				WriteIndented = true
				};
			}

		public async Task<IReadOnlyList<BacktestProfile>> GetAllAsync (
			CancellationToken cancellationToken = default )
			{
			cancellationToken.ThrowIfCancellationRequested ();

			List<BacktestProfile> profiles;

			lock (_sync)
				{
				if (!File.Exists (_filePath))
					{
					profiles = new List<BacktestProfile> ();
					}
				else
					{
					var json = File.ReadAllText (_filePath);
					profiles = string.IsNullOrWhiteSpace (json)
						? new List<BacktestProfile> ()
						: (JsonSerializer.Deserialize<List<BacktestProfile>> (json, _jsonOptions)
						   ?? new List<BacktestProfile> ());
					}
				}

			// Гарантируем наличие baseline-профиля.
			if (!profiles.Any (p => string.Equals (p.Id, "baseline", StringComparison.OrdinalIgnoreCase)))
				{
				var baselineConfig = BacktestConfigFactory.CreateBaseline ();

				var baselineProfile = new BacktestProfile
					{
					Id = "baseline",
					Name = "Baseline",
					Description = "Системный профиль по умолчанию (CreateBaseline).",
					IsSystem = true,
					Category = "system",
					IsFavorite = true,
					Config = baselineConfig
					};

				profiles.Add (baselineProfile);
				await SaveAllInternalAsync (profiles, cancellationToken).ConfigureAwait (false);
				}

			return profiles;
			}

		public async Task<BacktestProfile?> GetByIdAsync (
			string id,
			CancellationToken cancellationToken = default )
			{
			if (string.IsNullOrWhiteSpace (id))
				return null;

			var all = await GetAllAsync (cancellationToken).ConfigureAwait (false);
			return all.FirstOrDefault (p =>
				string.Equals (p.Id, id, StringComparison.OrdinalIgnoreCase));
			}

		public async Task<BacktestProfile> SaveAsync (
			BacktestProfile profile,
			CancellationToken cancellationToken = default )
			{
			if (profile == null) throw new ArgumentNullException (nameof (profile));
			if (string.IsNullOrWhiteSpace (profile.Id))
				throw new ArgumentException ("Profile.Id must be non-empty.", nameof (profile));

			cancellationToken.ThrowIfCancellationRequested ();

			var all = (await GetAllAsync (cancellationToken).ConfigureAwait (false)).ToList ();

			var idx = all.FindIndex (p =>
				string.Equals (p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));

			if (idx >= 0)
				{
				all[idx] = profile;
				}
			else
				{
				all.Add (profile);
				}

			await SaveAllInternalAsync (all, cancellationToken).ConfigureAwait (false);
			return profile;
			}

		private Task SaveAllInternalAsync (
			List<BacktestProfile> profiles,
			CancellationToken cancellationToken )
			{
			cancellationToken.ThrowIfCancellationRequested ();

			lock (_sync)
				{
				var json = JsonSerializer.Serialize (profiles, _jsonOptions);
				File.WriteAllText (_filePath, json);
				}

			return Task.CompletedTask;
			}
		}
	}
