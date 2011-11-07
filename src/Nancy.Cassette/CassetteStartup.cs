﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Reflection;
using Cassette;
using Cassette.IO;
using Cassette.UI;
using Nancy.Bootstrapper;
using TinyIoC;
using Utility.Logging;

namespace Nancy.Cassette
{
  public class CassetteStartup : IStartup
  {
    public CassetteStartup(TinyIoCContainer container)
    {
      this.container = container;
    }

    public IEnumerable<TypeRegistration> TypeRegistrations
    {
      get { return null; }
    }

    public IEnumerable<CollectionTypeRegistration> CollectionTypeRegistrations
    {
      get { return null; }
    }

    public IEnumerable<InstanceRegistration> InstanceRegistrations
    {
      get { return null; }
    }

    public void Initialize(IPipelines pipelines)
    {
      var configurations = container.ResolveAll<ICassetteConfiguration>().ToList();
      var rootDirectory = container.Resolve<IRootPathProvider>().GetRootPath();
      var cache = new IsolatedStorageDirectory(IsolatedStorageFile.GetMachineStoreForAssembly());

      Func<CassetteApplication> createApplication =
        () => new CassetteApplication(
                configurations,
                new FileSystemDirectory(rootDirectory),
                cache,
                new UrlAndPathGenerator(),
                ShouldOptimizeOutput,
                GetConfigurationVersion(configurations),
                rootDirectory,
                logger);

      applicationContainer = ShouldOptimizeOutput ? new CassetteApplicationContainer<CassetteApplication>(createApplication)
                                                    : new CassetteApplicationContainer<CassetteApplication>(createApplication, rootDirectory);

      Assets.GetApplication = () => applicationContainer.Application;

      pipelines.BeforeRequest.AddItemToStartOfPipeline(RunCassetteHandlers);
      pipelines.AfterRequest.AddItemToEndOfPipeline(RewriteResponseContents);
    }

    private static string GetConfigurationVersion(IEnumerable<ICassetteConfiguration> configurations)
    {
      var assemblyVersion = configurations
        .Select(configuration => new AssemblyName(configuration.GetType().Assembly.FullName).Version.ToString())
        .Distinct();

      //var parts = assemblyVersion.Concat(new[] { basePath });
      return string.Join("|", assemblyVersion);
    }
    
    private void RewriteResponseContents(NancyContext context)
    {
      applicationContainer.Application.RewriteResponseContents(context);
    }

    private Response RunCassetteHandlers(NancyContext context)
    {
      return applicationContainer.Application.RunCassetteHandlers(context);
    }

    public static bool ShouldOptimizeOutput
    {
      get { return shouldOptimizeOutput ?? (bool) (shouldOptimizeOutput = !GetDebugMode()); }
      set { shouldOptimizeOutput = value; }
    }

    public static ILogger Logger
    {
      set { logger = value.GetCurrentClassLogger(); }
    }

    private static bool GetDebugMode()
    {
      try
      {
        var attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof (DebuggableAttribute), true);
        if (attributes.Length == 0)
        {
          return false;
        }

        var debuggable = (DebuggableAttribute) attributes[0];
        return debuggable.IsJITTrackingEnabled;
      }
      catch (Exception)
      {
        return false;
      }
    }

    private readonly TinyIoCContainer container;
    private CassetteApplicationContainer<CassetteApplication> applicationContainer;

    private static bool? shouldOptimizeOutput;
    private static ILogger logger;

    private readonly List<Func<NancyContext, Response>> cassetteHandlers = new List<Func<NancyContext, Response>>();
  }
}