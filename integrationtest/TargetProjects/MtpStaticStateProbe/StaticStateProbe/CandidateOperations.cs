namespace StaticStateProbe;

/// <summary>
/// Each operation carries an ordinary relational mutation. No test asserts on the return value, so
/// every mutant here MUST survive. One is reported killed only if its test inherited poisoned
/// process-global startup state from an earlier mutant on the same reused test host.
/// </summary>
public static class CandidateOperations
{
    public static bool Candidate01(int value) => value > 1;
    public static bool Candidate02(int value) => value > 2;
    public static bool Candidate03(int value) => value > 3;
    public static bool Candidate04(int value) => value > 4;
    public static bool Candidate05(int value) => value > 5;
    public static bool Candidate06(int value) => value > 6;
    public static bool Candidate07(int value) => value > 7;
    public static bool Candidate08(int value) => value > 8;
    public static bool Candidate09(int value) => value > 9;
    public static bool Candidate10(int value) => value > 10;
    public static bool Candidate11(int value) => value > 11;
    public static bool Candidate12(int value) => value > 12;
}
