using System.IO;
using NUnit.Framework;
using UnityEngine;

// 이 어셈블리의 모든 PlayMode 테스트에 적용된다(네임스페이스 밖의 SetUpFixture).
// 게임 씬에서 판이 Result에 닿으면 PerfRuntime이 라운드 성능 기록을 쓰는데, 테스트 중의 기록은 사용자의
// persistentDataPath가 아니라 임시 폴더로 보내고 테스트가 끝나면 지운다.
[SetUpFixture]
public sealed class PerfTestOutputSetUp
{
    public static string Folder => Path.Combine(Application.temporaryCachePath, "RebootPerfTestOutput");

    [OneTimeSetUp]
    public void RedirectRoundRecordings()
    {
        PerfOutput.DirectoryOverride = Folder;
    }

    // 폴더만 지우고 경로는 그대로 둔다. Play Mode가 끝날 때 남은 쓰기가 있어도 persistentDataPath로 가지 않게 하며,
    // 다음 Play Mode가 시작될 때 PerfOutput이 스스로 비운다.
    [OneTimeTearDown]
    public void DeleteRoundRecordings()
    {
        if (Directory.Exists(Folder))
            Directory.Delete(Folder, true);
    }
}
