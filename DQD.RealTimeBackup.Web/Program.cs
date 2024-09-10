using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Serialization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Autofac;
using Autofac.Extensions.DependencyInjection;

using Bytewizer.Backblaze.Client;

using DQD.Backblaze.Agent.Autofac;

using DQD.CommandLineParser;

using DQD.RealTimeBackup.Diagnostics;
using DQD.RealTimeBackup.StateCache;
using DQD.RealTimeBackup.Storage;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.Web
{
	class Program
	{
		static void Main(string[] args)
		{
			var arguments = new CommandLine().Parse<CommandLineArguments>();

			var parameters = LoadOperatingParameters();

			Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");

			var builder = Host.CreateDefaultBuilder(args);

			var host = builder
				.UseServiceProviderFactory(new AutofacServiceProviderFactory())
				.ConfigureWebHostDefaults(
					webHostBuilder =>
					{
						webHostBuilder.UseUrls(parameters.WebAccessServerURLs);

						if (!string.IsNullOrWhiteSpace(parameters.WebAccessCertificatePath)
						 && Directory.Exists(parameters.WebAccessCertificatePath))
						{
							webHostBuilder.ConfigureKestrel(
								serverOptions =>
								{
									serverOptions.ConfigureHttpsDefaults(
										httpsOptions =>
										{
											httpsOptions.AllowAnyClientCertificate();
											httpsOptions.ServerCertificate = LoadServerCertificate(parameters.WebAccessCertificatePath);
										});
								});
						}

						string? myPath = Path.GetDirectoryName(typeof(Program).Assembly.Location);

						webHostBuilder.UseWebRoot(Path.Combine(myPath ?? "/", "Static"));

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
						configure.RegisterType<RemoteFileStateCache>().AsImplementedInterfaces().InstancePerDependency();

						configure.RegisterType<RemoteFileStateCacheRemoteStorage>().AsImplementedInterfaces().InstancePerDependency();

						configure.RegisterType<SessionManager>().AsImplementedInterfaces().SingleInstance();

						configure.RegisterType<AuthenticateWithBackblazeFilter>().AsImplementedInterfaces().InstancePerDependency();
					})
				.Build();

			if (arguments.Daemon)
			{
				// We can't literally fork the process, because that'll only bring the calling thread across. But, the child
				// will repeat exactly the same initialization, so if it was going to fail, it already would have.
				var psi = new ProcessStartInfo();

				psi.FileName = Process.GetCurrentProcess().MainModule!.FileName;

				if (psi.FileName.EndsWith("/dotnet"))
					psi.Arguments = typeof(Program).Assembly.Location;

				Process.Start(psi);

				return;
			}

			host.Run();
		}

		static X509Certificate2 LoadServerCertificate(string path)
		{
			string certificatePem = File.ReadAllText(Path.Combine(path, "fullchain.pem"));
			string privateKeyPem = File.ReadAllText(Path.Combine(path, "privkey.pem"));

			return X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
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
				using (var stream = File.OpenRead(DQD.RealTimeBackup.CommandLineArguments.DefaultConfigurationPath))
					parameters = (OperatingParameters)serializer.Deserialize(stream)!;
			}
			catch (Exception e)
			{
				throw new Exception("Unable to parse configuration file: " + DQD.RealTimeBackup.CommandLineArguments.DefaultConfigurationPath, e);
			}

			return parameters;
		}
	}
}

