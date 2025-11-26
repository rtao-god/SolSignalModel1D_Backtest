using System;
using System.Net;
using System.Net.Http;

namespace SolSignalModel1D_Backtest.Core.Infra
	{
	public static class HttpFactory
		{
		public static HttpClient CreateDefault ( string ua )
			{
			var handler = new SocketsHttpHandler
				{
				PooledConnectionLifetime = TimeSpan.FromMinutes (5),
				AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
				};
			var http = new HttpClient (handler)
				{
				Timeout = TimeSpan.FromSeconds (40)
				};
			http.DefaultRequestHeaders.UserAgent.ParseAdd (ua);
			http.DefaultRequestHeaders.Accept.ParseAdd ("application/json");
			return http;
			}
		}
	}
