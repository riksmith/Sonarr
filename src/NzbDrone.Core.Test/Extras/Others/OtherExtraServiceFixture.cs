using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Others
{
    [TestFixture]
    public class OtherExtraServiceFixture : CoreTest<OtherExtraService>
    {
        private Series _series;
        private EpisodeFile _episodeFile;
        private LocalEpisode _localEpisode;

        private string _seriesFolder;
        private string _episodeFolder;

        [SetUp]
        public void Setup()
        {
            _seriesFolder = @"C:\Test\TV\Series Title".AsOsAgnostic();
            _episodeFolder = @"C:\Test\Unsorted TV\Series.Title.S01".AsOsAgnostic();

            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Path = _seriesFolder)
                                     .Build();

            var episodes = Builder<Episode>.CreateListOfSize(1)
                                           .All()
                                           .With(e => e.SeasonNumber = 1)
                                           .Build()
                                           .ToList();

            _episodeFile = Builder<EpisodeFile>.CreateNew()
                                               .With(f => f.Path = Path.Combine(_series.Path, "Season 1", "Series Title - S01E01.mkv").AsOsAgnostic())
                                               .With(f => f.RelativePath = @"Season 1\Series Title - S01E01.mkv")
                                               .Build();

            _localEpisode = Builder<LocalEpisode>.CreateNew()
                                                 .With(l => l.Series = _series)
                                                 .With(l => l.Episodes = episodes)
                                                 .With(l => l.Path = Path.Combine(_episodeFolder, "Series.Title.S01E01.mkv").AsOsAgnostic())
                                                 .With(l => l.FileEpisodeInfo = new ParsedEpisodeInfo
                                                 {
                                                     SeasonNumber = 1,
                                                     EpisodeNumbers = new[] { 1 }
                                                 })
                                                 .Build();
        }

        [Test]
        [TestCase("Series Title - S01E01.nfo", "Series Title - S01E01.nfo")]
        [TestCase("Series.Title.S01E01.nfo", "Series Title - S01E01.nfo")]
        [TestCase("Series-Title-S01E01.nfo", "Series Title - S01E01.nfo")]
        [TestCase("Series Title S01E01.nfo", "Series Title - S01E01.nfo")]
        [TestCase("Series_Title_S01E01.nfo", "Series Title - S01E01.nfo")]
        [TestCase("S01E01.thumb.jpg", "Series Title - S01E01.jpg")]
        [TestCase(@"Series.Title.S01E01\thumb.jpg", "Series Title - S01E01.jpg")]
        [TestCase("Series.Title.S01E01.xml", "Series Title - S01E01.xml")]
        [TestCase("Series.Title.S01E01-mediainfo.xml", "Series Title - S01E01-mediainfo.xml")]
        [TestCase("Series.Title.S01E01.mkv.mediainfo.xml", "Series Title - S01E01.mkv.mediainfo.xml")]
        [TestCase("Series.Title.S01E01.avi.mediainfo.xml", "Series Title - S01E01.mkv.mediainfo.xml")]
        public void should_import_matching_file(string filePath, string expectedOutputPath)
        {
            var files = new List<string> { Path.Combine(_episodeFolder, filePath).AsOsAgnostic() };

            var results = Subject.ImportFiles(_localEpisode, _episodeFile, files, true).ToList();

            results.Count.Should().Be(1);

            results[0].RelativePath.AsOsAgnostic().PathEquals(Path.Combine("Season 1", expectedOutputPath).AsOsAgnostic()).Should().Be(true);
        }

        [Test]
        public void should_not_import_multiple_nfo_files()
        {
            var files = new List<string>
            {
                Path.Combine(_episodeFolder, "Series.Title.S01E01.nfo").AsOsAgnostic(),
                Path.Combine(_episodeFolder, "Series_Title_S01E01.nfo").AsOsAgnostic(),
            };

            var results = Subject.ImportFiles(_localEpisode, _episodeFile, files, true).ToList();

            results.Count.Should().Be(1);
        }

        [Test]
        [TestCase(@"Season 1\Old Name - S01E01.xml", "Series Title - S01E01.mkv", "Series Title - S01E01.xml")]
        [TestCase(@"Season 1\Old Name - S01E01.nfo", "Series Title - S01E01.mkv", "Series Title - S01E01.nfo")]
        [TestCase(@"Season 1\Old Name - S01E01-mediainfo.xml", "Series Title - S01E01.mkv", "Series Title - S01E01-mediainfo.xml")]
        [TestCase(@"Season 1\Old Name - S01E01-MediaInfo.XML", "Series Title - S01E01.mkv", "Series Title - S01E01-MediaInfo.XML")]
        [TestCase(@"Season 1\Old Name - S01E01.mkv.mediainfo.xml", "Series Title - S01E01.mkv", "Series Title - S01E01.mkv.mediainfo.xml")]
        [TestCase(@"Season 1\Old Name - S01E01.mkv.mediainfo.xml", "Series Title - S01E01.disc", "Series Title - S01E01.disc.mediainfo.xml")]
        [TestCase(@"Season 1\Old Name - S01E01-mediainfo.xml-orig", "Series Title - S01E01.mkv", "Series Title - S01E01.xml-orig")]
        public void should_rename_extra_file_preserving_known_suffix(string extraRelativePath, string episodeFileName, string expectedFileName)
        {
            _episodeFile.Id = 1;
            _episodeFile.RelativePath = Path.Combine("Season 1", episodeFileName).AsOsAgnostic();

            var extraFile = new OtherExtraFile
            {
                Id = 1,
                SeriesId = _series.Id,
                EpisodeFileId = _episodeFile.Id,
                RelativePath = extraRelativePath.AsOsAgnostic(),
                Extension = Path.GetExtension(extraRelativePath)
            };

            Mocker.GetMock<IOtherExtraFileService>()
                  .Setup(s => s.GetFilesBySeries(_series.Id))
                  .Returns(new List<OtherExtraFile> { extraFile });

            Subject.MoveFilesAfterRename(_series, new List<EpisodeFile> { _episodeFile });

            var expectedPath = Path.Combine(_seriesFolder, "Season 1", expectedFileName).AsOsAgnostic();

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.MoveFile(Path.Combine(_seriesFolder, extraRelativePath).AsOsAgnostic(), expectedPath, It.IsAny<bool>()), Times.Once());

            extraFile.RelativePath.Should().Be(Path.Combine("Season 1", expectedFileName).AsOsAgnostic());
        }

        [Test]
        public void should_not_move_extra_file_with_suffix_that_already_has_the_correct_name()
        {
            _episodeFile.Id = 1;
            _episodeFile.RelativePath = @"Season 1\Series Title - S01E01.mkv".AsOsAgnostic();

            var extraFile = new OtherExtraFile
            {
                Id = 1,
                SeriesId = _series.Id,
                EpisodeFileId = _episodeFile.Id,
                RelativePath = @"Season 1\Series Title - S01E01-mediainfo.xml".AsOsAgnostic(),
                Extension = ".xml"
            };

            Mocker.GetMock<IOtherExtraFileService>()
                  .Setup(s => s.GetFilesBySeries(_series.Id))
                  .Returns(new List<OtherExtraFile> { extraFile });

            Subject.MoveFilesAfterRename(_series, new List<EpisodeFile> { _episodeFile });

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
