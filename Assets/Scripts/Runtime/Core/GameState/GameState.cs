// 한 라운드의 시작부터 결과 화면까지 사용되는 공개 게임 상태다.
public enum GameState
{
    Initializing,
    Ready,
    IntroDialogue,
    Countdown,
    Battle,
    Paused,
    Finishing,
    OutroDialogue,
    Result
}
