using System;
using SysBot.Base;

namespace SysBot.Pokemon;

/// <summary>
/// Tracks the state of the bot and what it should execute next.
/// </summary>
[Serializable]
public sealed class PokeBotState : BotState<PokeRoutineType, SwitchConnectionConfig>
{
    /// <summary>
    /// Optional per-bot game mode. When <see langword="null"/>, falls back to <see cref="ProgramConfig.Mode"/>.
    /// </summary>
    public GameMode? Mode { get; set; }

    /// <inheritdoc/>
    public override void IterateNextRoutine() => CurrentRoutineType = NextRoutineType;
    /// <inheritdoc/>
    public override void Initialize() => Resume();
    /// <inheritdoc/>
    public override void Pause() => NextRoutineType = PokeRoutineType.Idle;
    /// <inheritdoc/>
    public override void Resume() => NextRoutineType = InitialRoutine;
}
