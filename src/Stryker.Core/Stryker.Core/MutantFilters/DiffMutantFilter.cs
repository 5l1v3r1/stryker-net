﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Stryker.Core.Clients;
using Stryker.Core.DashboardCompare;
using Stryker.Core.DiffProviders;
using Stryker.Core.Initialisation;
using Stryker.Core.Logging;
using Stryker.Core.Mutants;
using Stryker.Core.Mutators;
using Stryker.Core.Options;
using Stryker.Core.ProjectComponents;
using Stryker.Core.Reporters.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Stryker.Core.MutantFilters
{
    public class DiffMutantFilter : IMutantFilter
    {
        private readonly DiffResult _diffResult;
        private readonly IDashboardClient _dashboardClient;
        private readonly IBranchProvider _branchProvider;

        private readonly StrykerOptions _options;

        private readonly JsonReport _baseline;

        private ILogger<DiffMutantFilter> _logger;

        public string DisplayName => "git diff file filter";

        public DiffMutantFilter(StrykerOptions options, IDiffProvider diffProvider, IDashboardClient dashboardClient = null, IBranchProvider branchProvider = null)
        {
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<DiffMutantFilter>();

            _dashboardClient = dashboardClient ?? new DashboardClient(options);
            _branchProvider = branchProvider ?? new GitBranchProvider(options);
            _options = options;

            if (options.DiffCompareToDashboard)
            {
                _baseline = GetBaseline().Result;
            }


            if (options.DiffEnabled)
            {
                _diffResult = diffProvider.ScanDiff();
            }
        }
        
        public IEnumerable<Mutant> FilterMutants(IEnumerable<Mutant> mutants, FileLeaf file, StrykerOptions options)
        {
            if (options.DiffCompareToDashboard)
            {
                if (_baseline == null)
                {
                    return mutants;
                } else
                {
                    UpdateMutantsWithBaseline(mutants, file);
                }
            }

            if (options.DiffEnabled && !_diffResult.TestsChanged)
            {
                if (_diffResult.ChangedFiles.Contains(file.FullPath))
                {
                    foreach(var mutant in mutants)
                    {
                        mutant.ResultStatus = MutantStatus.NotRun;
                    }
                    return mutants;
                }
                return Enumerable.Empty<Mutant>();
            }
            return mutants;
        }

        private async Task<JsonReport> GetBaseline()
        {
            var branchName = _branchProvider.GetCurrentBranchCanonicalName();

            _options.CurrentBranchCanonicalName = branchName;

            var report = await _dashboardClient.PullReport(branchName);

            if (report == null)
            {
                _logger.LogInformation("We could not locate a baseline for project {0}, now trying fallback Version", _options.ProjectName);

                return await GetFallbackBaseline();
            }

            _logger.LogInformation("Found report of project {0} using version {1} ", _options.ProjectName, branchName);

            return report;
        }

        public async Task<JsonReport> GetFallbackBaseline()
        {
            var report = await _dashboardClient.PullReport(_options.FallbackVersion);

            if (report == null)
            {
                _logger.LogInformation("We could not locate a baseline for project using fallback version. Now running a complete test to establish a baseline.");
                return null;
            }
            else
            {
                _logger.LogInformation("Found report of project {0} using version {1}", _options.ProjectName, _options.FallbackVersion);

                return report;
            }
        }

        private void UpdateMutantsWithBaseline(IEnumerable<Mutant> mutants, FileLeaf file)
        {
            foreach(var baselineFile in _baseline.Files)
            {
                var filePath = FilePathUtils.NormalizePathSeparators(baselineFile.Key);

                if (filePath == file.RelativePath)
                {
                    foreach (var baselineMutant in baselineFile.Value.Mutants)
                    {
                        var baselineMutantSourceCode = GetMutantSourceCode(baselineFile.Value.Source, baselineMutant);

                        var mutant = mutants.FirstOrDefault(x => x.Mutation.OriginalNode.ToString() == baselineMutantSourceCode 
                        && x.Mutation.DisplayName ==  baselineMutant.MutatorName);

                        if (mutant != null)
                        {
                            mutant.ResultStatus = (MutantStatus)Enum.Parse(typeof(MutantStatus), baselineMutant.Status);
                        }
                    }
                }
            }
        }


        public string GetMutantSourceCode(string source, JsonMutant mutant)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);

            var beginLinePosition = new LinePosition(mutant.Location.Start.Line - 1, mutant.Location.Start.Column - 1);
            var endLinePosition = new LinePosition(mutant.Location.End.Line - 1, mutant.Location.End.Column - 1);

            LinePositionSpan span = new LinePositionSpan(beginLinePosition, endLinePosition);

            var textSpan = tree.GetText().Lines.GetTextSpan(span);

            return tree.GetRoot().DescendantNodes(textSpan)
                .First(n => textSpan.Equals(n.Span)).ToString();

        }
    }
}
