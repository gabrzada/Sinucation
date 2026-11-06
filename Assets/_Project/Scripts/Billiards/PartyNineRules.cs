using System.Collections.Generic;

namespace FriendSlop.Billiards
{
    public enum FoulType
    {
        None,
        NoContact,
        WrongBallFirst,
        Scratch
    }

    /// <summary>Tudo o que aconteceu numa tacada, preenchido pela simulação autoritativa.</summary>
    public sealed class ShotRecord
    {
        public int ShooterId;
        public int ShotSequence;
        public int LowestBallAtStart;
        public int FinalBall;
        public int FirstContact = -1;
        public readonly List<int> Pocketed = new List<int>();
        public readonly List<string> ActiveModifierIds = new List<string>();
        public bool CueScratched;
        public bool IsBreak;
        public int CushionHits;
        public bool Aborted;

        public bool FinalPocketed => Pocketed.Contains(FinalBall);

        public int ObjectBallsPocketed
        {
            get
            {
                int count = 0;
                foreach (int n in Pocketed)
                    if (n > 0) count++;
                return count;
            }
        }
    }

    public struct RuleOutcome
    {
        public FoulType Foul;
        public bool KeepTurn;
        public bool RoundWon;
        public bool RespotFinalBall;
        public bool BallInHandNext;
        public int LegalPockets;

        public bool IsFoul => Foul != FoulType.None;
    }

    /// <summary>
    /// Perfil "Party Nine": contactar primeiro a menor bola activa → encaçapamento legal mantém turno →
    /// falha passa turno → scratch/falta dá bola-na-mão → bola final legal vence a rodada.
    /// Não há falta de "bola fora da mesa": a física é plana (2D) e as tabelas são contínuas, por isso nenhuma bola
    /// salta; uma bola que passe a linha das tabelas só o pode fazer pela boca de uma caçapa, e conta como encaçapada
    /// (ver BilliardsSimulation.PocketAt e Docs/DECISOES.md na raiz do projecto).
    /// </summary>
    public static class PartyNineRules
    {
        public static RuleOutcome Evaluate(ShotRecord r)
        {
            var o = new RuleOutcome();

            if (r.CueScratched) o.Foul = FoulType.Scratch;
            else if (r.FirstContact < 0) o.Foul = FoulType.NoContact;
            else if (r.FirstContact != r.LowestBallAtStart) o.Foul = FoulType.WrongBallFirst;

            if (o.IsFoul)
            {
                o.RespotFinalBall = r.FinalPocketed;
                o.BallInHandNext = true;
                return o;
            }

            o.LegalPockets = r.ObjectBallsPocketed;
            if (r.FinalPocketed)
            {
                o.RoundWon = true;
                return o;
            }

            o.KeepTurn = o.LegalPockets > 0;
            return o;
        }

        public static string Describe(FoulType foul)
        {
            switch (foul)
            {
                case FoulType.NoContact: return "Falta: a branca não tocou em nenhuma bola";
                case FoulType.WrongBallFirst: return "Falta: tocou primeiro na bola errada";
                case FoulType.Scratch: return "Falta: branca encaçapada";
                default: return string.Empty;
            }
        }
    }
}
