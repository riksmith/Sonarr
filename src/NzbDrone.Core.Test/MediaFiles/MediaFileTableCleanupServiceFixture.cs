using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class MediaFileTableCleanupServiceFixture : CoreTest<MediaFileTableCleanupService>
    {
        private const string DELETED_PATH = "ANY FILE WITH THIS PATH IS CONSIDERED DELETED!";
        private List<Episode> _episodes;
        private Series _series;

        [SetUp]
        public void SetUp()
        {
            _episodes = Builder<Episode>.CreateListOfSize(10)
                  .Build()
                  .ToList();

            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Path = @"C:\Test\TV\Series".AsOsAgnostic())
                                     .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(e => e.FileExists(It.Is<string>(c => !c.Contains(DELETED_PATH))))
                  .Returns(true);

            Mocker.GetMock<IEpisodeService>()
                  .Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                  .Returns(_episodes);
        }

        private void GivenEpisodeFiles(IEnumerable<EpisodeFile> episodeFiles)
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(c => c.GetFilesBySeries(It.IsAny<int>()))
                  .Returns(episodeFiles.ToList());
        }

        private void GivenFilesAreNotAttachedToEpisode()
        {
            _episodes.ForEach(e => e.EpisodeFileId = 0);

            Mocker.GetMock<IEpisodeService>()
                  .Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                  .Returns(_episodes);
        }

        private List<string> FilesOnDisk(IEnumerable<EpisodeFile> episodeFiles)
        {
            return episodeFiles.Select(e => Path.Combine(_series.Path, e.RelativePath)).ToList();
        }

        [Test]
        public void should_skip_files_that_exist_in_disk()
        {
            var episodeFiles = Builder<EpisodeFile>.CreateListOfSize(10)
                .Build();

            GivenEpisodeFiles(episodeFiles);

            Subject.Clean(_series, FilesOnDisk(episodeFiles));

            Mocker.GetMock<IEpisodeService>().Verify(c => c.UpdateEpisode(It.IsAny<Episode>()), Times.Never());
        }

        [Test]
        public void should_delete_non_existent_files()
        {
            var episodeFiles = Builder<EpisodeFile>.CreateListOfSize(10)
                .Random(2)
                .With(c => c.RelativePath = DELETED_PATH)
                .Build();

            GivenEpisodeFiles(episodeFiles);

            Subject.Clean(_series, FilesOnDisk(episodeFiles.Where(e => e.RelativePath != DELETED_PATH)));

            Mocker.GetMock<IMediaFileService>().Verify(c => c.Delete(It.Is<EpisodeFile>(e => e.RelativePath == DELETED_PATH), DeleteMediaFileReason.MissingFromDisk), Times.Exactly(2));
        }

        [Test]
        public void should_delete_files_that_dont_belong_to_any_episodes()
        {
            var episodeFiles = Builder<EpisodeFile>.CreateListOfSize(10)
                                .Random(10)
                                .With(c => c.RelativePath = "ExistingPath")
                                .Build();

            GivenEpisodeFiles(episodeFiles);
            GivenFilesAreNotAttachedToEpisode();

            Subject.Clean(_series, FilesOnDisk(episodeFiles));

            Mocker.GetMock<IMediaFileService>().Verify(c => c.Delete(It.IsAny<EpisodeFile>(), DeleteMediaFileReason.NoLinkedEpisodes), Times.Exactly(10));
        }

        [Test]
        public void should_unlink_episode_when_episodeFile_does_not_exist()
        {
            GivenEpisodeFiles(new List<EpisodeFile>());

            Subject.Clean(_series, new List<string>());

            Mocker.GetMock<IEpisodeService>().Verify(c => c.UpdateEpisode(It.Is<Episode>(e => e.EpisodeFileId == 0)), Times.Exactly(10));
        }

        [Test]
        public void should_not_update_episode_when_episodeFile_exists()
        {
            var episodeFiles = Builder<EpisodeFile>.CreateListOfSize(10)
                                .Random(10)
                                .With(c => c.RelativePath = "ExistingPath")
                                .Build();

            GivenEpisodeFiles(episodeFiles);

            Subject.Clean(_series, FilesOnDisk(episodeFiles));

            Mocker.GetMock<IEpisodeService>().Verify(c => c.UpdateEpisode(It.IsAny<Episode>()), Times.Never());
        }

        private EpisodeFile GivenEpisodeFileWithEpisode(int id, string relativePath)
        {
            _episodes[id - 1].EpisodeFileId = id;

            return Builder<EpisodeFile>.CreateNew()
                                       .With(f => f.Id = id)
                                       .With(f => f.RelativePath = relativePath.AsOsAgnostic())
                                       .Build();
        }

        private string SeriesFilePath(string relativePath)
        {
            return Path.Combine(_series.Path, relativePath.AsOsAgnostic());
        }

        [Test]
        public void should_update_episode_file_to_disc_stub_when_video_is_replaced()
        {
            var episodeFile = GivenEpisodeFileWithEpisode(1, @"Season 1\Series - S01E01.mkv");

            GivenEpisodeFiles(new[] { episodeFile });

            Subject.Clean(_series, new List<string> { SeriesFilePath(@"Season 1\Series - S01E01.disc") });

            Mocker.GetMock<IMediaFileService>().Verify(c => c.Update(It.Is<EpisodeFile>(e => e.Id == 1 && e.RelativePath == @"Season 1\Series - S01E01.disc".AsOsAgnostic())), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(c => c.Delete(It.IsAny<EpisodeFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IEpisodeService>().Verify(c => c.UpdateEpisode(It.Is<Episode>(e => e.Id == _episodes[0].Id)), Times.Never());
        }

        [Test]
        public void should_delete_missing_file_when_disc_stub_is_already_tracked()
        {
            var videoFile = GivenEpisodeFileWithEpisode(1, @"Season 1\Series - S01E01.mkv");
            var discFile = GivenEpisodeFileWithEpisode(2, @"Season 1\Series - S01E01.disc");

            GivenEpisodeFiles(new[] { videoFile, discFile });

            Subject.Clean(_series, new List<string> { SeriesFilePath(@"Season 1\Series - S01E01.disc") });

            Mocker.GetMock<IMediaFileService>().Verify(c => c.Update(It.IsAny<EpisodeFile>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(c => c.Delete(It.Is<EpisodeFile>(e => e.Id == 1), DeleteMediaFileReason.MissingFromDisk), Times.Once());
        }

        [Test]
        public void should_only_replace_one_missing_file_with_the_same_disc_stub()
        {
            var mkvFile = GivenEpisodeFileWithEpisode(1, @"Season 1\Series - S01E01.mkv");
            var mp4File = GivenEpisodeFileWithEpisode(2, @"Season 1\Series - S01E01.mp4");

            GivenEpisodeFiles(new[] { mkvFile, mp4File });

            Subject.Clean(_series, new List<string> { SeriesFilePath(@"Season 1\Series - S01E01.disc") });

            Mocker.GetMock<IMediaFileService>().Verify(c => c.Update(It.Is<EpisodeFile>(e => e.Id == 1)), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(c => c.Delete(It.Is<EpisodeFile>(e => e.Id == 2), DeleteMediaFileReason.MissingFromDisk), Times.Once());
        }

        [Test]
        public void should_delete_missing_disc_stub()
        {
            var discFile = GivenEpisodeFileWithEpisode(1, @"Season 1\Series - S01E01.disc");

            GivenEpisodeFiles(new[] { discFile });

            Subject.Clean(_series, new List<string>());

            Mocker.GetMock<IMediaFileService>().Verify(c => c.Update(It.IsAny<EpisodeFile>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(c => c.Delete(It.Is<EpisodeFile>(e => e.Id == 1), DeleteMediaFileReason.MissingFromDisk), Times.Once());
        }
    }
}
