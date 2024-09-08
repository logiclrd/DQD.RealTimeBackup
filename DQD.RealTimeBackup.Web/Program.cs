using System;
using System.IO;
using System.Xml.Serialization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Autofac;
using Autofac.Extensions.DependencyInjection;

using Bytewizer.Backblaze.Client;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.Utility;
using Microsoft.AspNetCore.StaticFiles;
using System.Collections.Generic;

namespace DQD.RealTimeBackup.Web
{
	class Program
	{
		static void Main(string[] args)
		{
			Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");

			var builder = Host.CreateDefaultBuilder(args);

			var host = builder
				.UseServiceProviderFactory(new AutofacServiceProviderFactory())
				.ConfigureWebHostDefaults(
					webHostBuilder =>
					{
						webHostBuilder.UseWebRoot("Static");

						webHostBuilder.Configure(
							app =>
							{
								app.UseRouting();
								app.UseEndpoints(builder => builder.MapControllers());

								app.UseDefaultFiles();
								app.UseStaticFiles(
									new StaticFileOptions()
									{
										ContentTypeProvider =
											new FileExtensionContentTypeProvider(
												new Dictionary<string, string>()
												{
													{ ".html", "text/html" },
													{ ".svg", "image/svg+xml" },
												}),
									});
							});

						webHostBuilder.ConfigureServices(
							services =>
							{
								services.AddControllers()
									.AddJsonOptions(
										options =>
										{
											options.JsonSerializerOptions.PropertyNamingPolicy = new PassThroughNamingPolicy();
										});
							});
					})
				.ConfigureContainer<ContainerBuilder>(
					configure =>
					{
						var parameters = LoadOperatingParameters();

						configure.RegisterInstance(parameters);

						var backblazeAgentOptions =
							new ClientOptions
							{
								KeyId = parameters.RemoteStorageKeyID,
								ApplicationKey = parameters.RemoteStorageApplicationKey
							};

						configure.AddBackblazeAgent(backblazeAgentOptions);

						configure.RegisterType<ContentKeyGenerator>().AsImplementedInterfaces().SingleInstance();
						configure.RegisterType<ErrorLogger>().AsImplementedInterfaces().SingleInstance();
						configure.RegisterType<B2RemoteStorage>().AsImplementedInterfaces().SingleInstance();

						configure.RegisterType<Timer>().AsImplementedInterfaces().SingleInstance();
						configure.RegisterType<CacheActionLog>().AsImplementedInterfaces().SingleInstance();
						configure.RegisterType<RemoteFileStateCache>().AsImplementedInterfaces().SingleInstance();

						configure.RegisterType<RemoteFileStateCacheRemoteStorage>().AsImplementedInterfaces().InstancePerDependency();

						configure.RegisterType<SessionManager>().AsImplementedInterfaces().SingleInstance();

						configure.RegisterType<AuthenticateWithBackblazeFilter>().AsImplementedInterfaces().InstancePerDependency();
					})
				.Build();

			host.Run();
		}

		class AuthenticateWithBackblazeFilter : IStartupFilter
		{
			IRemoteStorage _remoteStorage;

			public AuthenticateWithBackblazeFilter(IRemoteStorage remoteStorage)
			{
				_remoteStorage = remoteStorage;
			}

			public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
			{
				return
					(builder) =>
					{
						_remoteStorage.Authenticate();
						next(builder);
					};
			}
		}

		static OperatingParameters LoadOperatingParameters()
		{
			var serializer = new XmlSerializer(typeof(OperatingParameters));

			OperatingParameters parameters;

			try
			{
				using (var stream = File.OpenRead(CommandLineArguments.DefaultConfigurationPath))
					parameters = (OperatingParameters)serializer.Deserialize(stream)!;
			}
			catch (Exception e)
			{
				throw new Exception("Unable to parse configuration file: " + CommandLineArguments.DefaultConfigurationPath, e);
			}

			return parameters;
		}
	}
}

