﻿using CommandLine;
using Microwalk.Analysis;
using Microwalk.TestcaseGeneration;
using Microwalk.TraceGeneration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Helpers;
using System.Threading.Tasks.Dataflow;
using Microwalk.TracePreprocessing;
using System.Threading;

namespace Microwalk
{
    /// <summary>
    /// Main class. Contains initialization and pipeline logic.
    /// </summary>
    class Program
    {
        /// <summary>
        /// Contains information about the configured pipeline stage modules.
        /// </summary>
        private static readonly ModuleConfigurationData _moduleConfiguration = new ModuleConfigurationData();

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">Command line.</param>
        public static void Main(string[] args)
        {
            // Register modules
            // Testcase generation
            TestcaseStage.Factory.Register<TestcaseGeneration.Modules.RandomTestcaseGenerator>();
            // Trace generation
            TraceStage.Factory.Register<TraceGeneration.Modules.PinTraceGenerator>();
            // Trace preprocessing
            PreprocessorStage.Factory.Register<TracePreprocessing.Modules.PinTracePreprocessor>();
            // Analysis
            AnalysisStage.Factory.Register<Analysis.Modules.TraceDumper>();
            AnalysisStage.Factory.Register<Analysis.Modules.MemoryAccessMi>();

            // Parse command line and execute framework using these options
            Parser.Default.ParseArguments<CommandLineOptions>(args).WithParsed(
                opts => RunAsync(opts)
                        .ContinueWith((t) =>
                        {
                            if(t.IsFaulted)
                                Console.WriteLine(t.Exception.ToString());
                        })
                        .Wait()
            );
        }

        /// <summary>
        /// Executes the framework using the given command line options.
        /// </summary>
        /// <param name="commandLineOptions">Command line options.</param>
        static async Task RunAsync(CommandLineOptions commandLineOptions)
        {
            // Configuration file supplied?
            if(commandLineOptions.ConfigurationFile == null)
            {
                Console.WriteLine("Please specify a configuration file, as described in the documentation.");
                Console.WriteLine();

                // Print module list
                Console.WriteLine("Available modules:");
                Console.WriteLine("  Testcase generation:");
                foreach(var (name, description) in TestcaseStage.Factory.GetSupportedModules())
                    Console.WriteLine(new string(' ', 4) + name + new string(' ', Math.Max(1, 24 - name.Length)) + description);
                Console.WriteLine();
                Console.WriteLine("  Trace generation:");
                foreach(var (name, description) in TraceStage.Factory.GetSupportedModules())
                    Console.WriteLine(new string(' ', 4) + name + new string(' ', Math.Max(1, 24 - name.Length)) + description);
                Console.WriteLine();
                Console.WriteLine("  Trace preprocessing:");
                foreach(var (name, description) in PreprocessorStage.Factory.GetSupportedModules())
                    Console.WriteLine(new string(' ', 4) + name + new string(' ', Math.Max(1, 24 - name.Length)) + description);
                Console.WriteLine();
                Console.WriteLine("  Analysis:");
                foreach(var (name, description) in AnalysisStage.Factory.GetSupportedModules())
                    Console.WriteLine(new string(' ', 4) + name + new string(' ', Math.Max(1, 24 - name.Length)) + description);
                Console.WriteLine();

                // Done
                return;
            }

            // Load configuration file
            try
            {
                // Open file and read YAML
                YamlStream yaml = new YamlStream();
                using(var configFileStream = new StreamReader(File.Open(commandLineOptions.ConfigurationFile, FileMode.Open, FileAccess.Read, FileShare.Read)))
                    yaml.Load(configFileStream);
                var rootNode = (YamlMappingNode)yaml.Documents[0].RootNode;

                // Read general configuration first
                var generalConfigurationNode = rootNode.GetChildNodeWithKey("general");

                // Initialize logger
                if(generalConfigurationNode == null)
                    Logger.Initialize(null);
                else
                    Logger.Initialize((YamlMappingNode)generalConfigurationNode.GetChildNodeWithKey("logger"));
                await Logger.LogDebugAsync("Loaded configuration file, initialized logger");

                // Read stages
                await Logger.LogDebugAsync("Reading pipeline configuration");
                foreach(var mainNode in rootNode.Children)
                {
                    // Read key
                    switch(mainNode.Key.GetNodeString())
                    {
                        case "testcase":
                        {
                            await Logger.LogDebugAsync("Reading and applying 'testcase' stage configuration");

                            // There must be a module name node
                            string moduleName = mainNode.Value.GetChildNodeWithKey("module").GetNodeString();

                            // Check for options
                            var optionNode = (YamlMappingNode)mainNode.Value.GetChildNodeWithKey("module-options");

                            // Create module, if possible
                            _moduleConfiguration.TestcaseStageModule = await TestcaseStage.Factory.CreateAsync(moduleName, optionNode);

                            // Remember general stage options
                            _moduleConfiguration.TestcaseStageOptions = (YamlMappingNode)mainNode.Value.GetChildNodeWithKey("options");

                            break;
                        }

                        case "trace":
                        {
                            await Logger.LogDebugAsync("Reading and applying 'trace' stage configuration");

                            // There must be a module name node
                            string moduleName = mainNode.Value.GetChildNodeWithKey("module").GetNodeString();

                            // Check for options
                            var optionNode = (YamlMappingNode)mainNode.Value.GetChildNodeWithKey("module-options");

                            // Create module, if possible
                            _moduleConfiguration.TraceStageModule = await TraceStage.Factory.CreateAsync(moduleName, optionNode);

                            // Remember general stage options
                            _moduleConfiguration.TraceStageOptions = (YamlMappingNode)mainNode.Value.GetChildNodeWithKey("options");

                            break;
                        }

                        case "preprocess":
                        {
                            await Logger.LogDebugAsync("Reading and applying 'preprocess' stage configuration");

                            // There must be a module name node
                            string moduleName = mainNode.Value.GetChildNodeWithKey("module").GetNodeString();

                            // Check for options
                            var optionNode = (YamlMappingNode)mainNode.Value.GetChildNodeWithKey("module-options");

                            // Create module, if possible
                            _moduleConfiguration.PreprocessorStageModule = await PreprocessorStage.Factory.CreateAsync(moduleName, optionNode);

                            // Remember general stage options
                            _moduleConfiguration.PreprocessorStageOptions = (YamlMappingNode)mainNode.Value.GetChildNodeWithKey("options");

                            break;
                        }

                        case "analysis":
                        {
                            await Logger.LogDebugAsync("Reading and applying 'analysis' stage configuration");

                            // There must be a module list node
                            var moduleListNode = mainNode.Value.GetChildNodeWithKey("modules");
                            if(!(moduleListNode is YamlSequenceNode moduleListSequenceNode))
                                throw new ConfigurationException("Module list node does not contain a sequence.");
                            _moduleConfiguration.AnalysesStageModules = new List<AnalysisStage>();
                            foreach(YamlMappingNode moduleEntryNode in moduleListSequenceNode)
                            {
                                // There must be a module name node
                                string moduleName = moduleEntryNode.GetChildNodeWithKey("module").GetNodeString();

                                // Check for options
                                var optionNode = (YamlMappingNode)moduleEntryNode.GetChildNodeWithKey("module-options");

                                // Create module, if possible
                                _moduleConfiguration.AnalysesStageModules.Add(await AnalysisStage.Factory.CreateAsync(moduleName, optionNode));
                            }

                            // Remember general stage options
                            _moduleConfiguration.AnalysisStageOptions = (YamlMappingNode)mainNode.Value.GetChildNodeWithKey("options");

                            break;
                        }
                    }
                }

                // Check presence of needed pipeline modules
                await Logger.LogDebugAsync("Doing some sanity checks");
                if(_moduleConfiguration.TestcaseStageModule == null
                    || _moduleConfiguration.TraceStageModule == null
                    || _moduleConfiguration.PreprocessorStageModule == null
                    || !_moduleConfiguration.AnalysesStageModules.Any())
                    throw new ConfigurationException("Incomplete module specification. Make sure that there is at least one module for testcase generation, trace generation, preprocessing and analysis, respectively.");

                // Initialize pipeline stages
                // -> [buffer] -> trace -> [buffer]-> preprocess -> [buffer] -> analysis
                await Logger.LogDebugAsync("Initializing pipeline stages");
                var traceStageBuffer = new BufferBlock<TraceEntity>(new DataflowBlockOptions
                {
                    BoundedCapacity = _moduleConfiguration.TraceStageOptions.GetChildNodeWithKey("input-buffer-size").GetNodeInteger(1),
                    EnsureOrdered = true
                });
                var traceStage = new TransformBlock<TraceEntity, TraceEntity>(TraceStageFunc, new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = 1,
                    EnsureOrdered = true,
                    MaxDegreeOfParallelism = _moduleConfiguration.TraceStageModule.SupportsParallelism ? _moduleConfiguration.TraceStageOptions.GetChildNodeWithKey("max-parallel-threads").GetNodeInteger(1) : 1,
                });
                var preprocessorStageBuffer = new BufferBlock<TraceEntity>(new DataflowBlockOptions
                {
                    BoundedCapacity = _moduleConfiguration.PreprocessorStageOptions.GetChildNodeWithKey("input-buffer-size").GetNodeInteger(1),
                    EnsureOrdered = true
                });
                var preprocessorStage = new TransformBlock<TraceEntity, TraceEntity>(PreprocessorStageFunc, new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = 1,
                    EnsureOrdered = true,
                    MaxDegreeOfParallelism = _moduleConfiguration.PreprocessorStageModule.SupportsParallelism ? _moduleConfiguration.PreprocessorStageOptions.GetChildNodeWithKey("max-parallel-threads").GetNodeInteger(1) : 1,
                });
                var analysisStageBuffer = new BufferBlock<TraceEntity>(new DataflowBlockOptions
                {
                    BoundedCapacity = _moduleConfiguration.AnalysisStageOptions.GetChildNodeWithKey("input-buffer-size").GetNodeInteger(1),
                    EnsureOrdered = true
                });
                var analysisStage = new ActionBlock<TraceEntity>(AnalysisStageFunc, new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = 1,
                    EnsureOrdered = true,
                    MaxDegreeOfParallelism = _moduleConfiguration.AnalysesStageModules.All(asm => asm.SupportsParallelism) ? _moduleConfiguration.AnalysisStageOptions.GetChildNodeWithKey("max-parallel-threads").GetNodeInteger(1) : 1,
                });

                // Link pipeline stages
                await Logger.LogDebugAsync("Linking pipeline stages");
                var linkOptions = new DataflowLinkOptions
                {
                    PropagateCompletion = true
                };
                traceStageBuffer.LinkTo(traceStage, linkOptions);
                traceStage.LinkTo(preprocessorStageBuffer, linkOptions);
                preprocessorStageBuffer.LinkTo(preprocessorStage, linkOptions);
                preprocessorStage.LinkTo(analysisStageBuffer, linkOptions);
                analysisStageBuffer.LinkTo(analysisStage, linkOptions);

                // Start posting test cases
                await Logger.LogInfoAsync("Start testcase thread -> pipeline start");
                var testcaseTaskCancellationTokenSource = new CancellationTokenSource();
                var testcaseTask = PostTestcases(traceStageBuffer, testcaseTaskCancellationTokenSource.Token)
                                    .ContinueWith(async (t) =>
                                    {
                                        if(t.IsFaulted && !t.Exception.Flatten().InnerExceptions.Any(e => e is TaskCanceledException))
                                        {
                                            // Log exception
                                            await Logger.LogErrorAsync("Testcase generation has stopped due to an unhandled exception:");
                                            await Logger.LogErrorAsync(t.Exception.ToString());
                                            await Logger.LogWarningAsync("Pipeline execution will be continued with the existing test cases.");
                                        }
                                    }, testcaseTaskCancellationTokenSource.Token);

                // Handle pipeline exceptions
                try
                {
                    // Wait for all stages to complete
                    await analysisStage.Completion;
                    await Logger.LogInfoAsync("Pipeline completed.");

                    // Do final analysis steps
                    foreach(var module in _moduleConfiguration.AnalysesStageModules)
                        await module.FinishAsync();
                }
                catch(Exception ex)
                {
                    // Stop test case generator for clean exit
                    testcaseTaskCancellationTokenSource.Cancel();

                    // Log exception
                    await Logger.LogErrorAsync("An exception occured in the pipeline:");
                    await Logger.LogErrorAsync(ex.ToString());
                    await Logger.LogInfoAsync("Trying to stop gracefully");
                }

                // Wait for test case generator to stop
                try
                {
                    await testcaseTask;
                    await Logger.LogDebugAsync("Testcase thread completed.");
                }
                catch(TaskCanceledException)
                {
                    // Ignore
                }
                finally
                {
                    testcaseTaskCancellationTokenSource.Dispose();
                }

                // Do some cleanup
                await Logger.LogDebugAsync("Performing some clean up");
                await _moduleConfiguration.TestcaseStageModule.UninitAsync();
                await _moduleConfiguration.TraceStageModule.UninitAsync();
                await _moduleConfiguration.PreprocessorStageModule.UninitAsync();
                await Task.WhenAll(_moduleConfiguration.AnalysesStageModules.Select(module => module.UninitAsync()));

                // Done
                await Logger.LogInfoAsync("Program completed.");
            }
            catch(Exception ex)
            {
                // Use logger, if already initialized
                if(Logger.IsInitialized())
                {
                    await Logger.LogErrorAsync("A general error ocurred:");
                    await Logger.LogErrorAsync(ex.ToString());
                }
                else
                {
                    Console.WriteLine("A general error occured:");
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        /// <summary>
        /// Posts testcases into the first pipeline block, and completes the block afterwards.
        /// </summary>
        /// <param name="traceStageBuffer">First pipeline block.</param>
        /// <param name="token">Cancellation token to stop posting testcases, in case an exception is encountered.</param>
        /// <returns></returns>
        private static async Task PostTestcases(BufferBlock<TraceEntity> traceStageBuffer, CancellationToken token)
        {
            // Feed testcases into pipeline
            while(!await _moduleConfiguration.TestcaseStageModule.IsDoneAsync())
                await traceStageBuffer.SendAsync(await _moduleConfiguration.TestcaseStageModule.NextTestcaseAsync(token), token);

            // Mark first block as completed
            // This should propagate through the entire pipeline
            traceStageBuffer.Complete();
        }

        /// <summary>
        /// Trace stage implementation.
        /// </summary>
        /// <param name="t">Input trace entity.</param>
        /// <returns></returns>
        private static async Task<TraceEntity> TraceStageFunc(TraceEntity t)
        {
            // Run module
            await _moduleConfiguration.TraceStageModule.GenerateTraceAsync(t);
            return t;
        }

        /// <summary>
        /// Preprocessor stage implementation.
        /// </summary>
        /// <param name="t">Input trace entity.</param>
        /// <returns></returns>
        private static async Task<TraceEntity> PreprocessorStageFunc(TraceEntity t)
        {
            // Run module
            await _moduleConfiguration.PreprocessorStageModule.PreprocessTraceAsync(t);
            return t;
        }

        /// <summary>
        /// Analysis stage implementation.
        /// </summary>
        /// <param name="t">Input trace entity.</param>
        /// <returns></returns>
        private static async Task AnalysisStageFunc(TraceEntity t)
        {
            // Run modules in parallel
            await Task.WhenAll(_moduleConfiguration.AnalysesStageModules.Select(module => module.AddTraceAsync(t)));
        }

        /// <summary>
        /// Container class for pipeline stage module configuration.
        /// </summary>
        private class ModuleConfigurationData
        {
            public TestcaseStage TestcaseStageModule { get; set; }
            public TraceStage TraceStageModule { get; set; }
            public PreprocessorStage PreprocessorStageModule { get; set; }
            public List<AnalysisStage> AnalysesStageModules { get; set; }
            public YamlMappingNode TestcaseStageOptions { get; set; }
            public YamlMappingNode TraceStageOptions { get; set; }
            public YamlMappingNode PreprocessorStageOptions { get; set; }
            public YamlMappingNode AnalysisStageOptions { get; set; }
        }
    }
}
