namespace CodingAgent.Domain;

public enum AgentRunStatus
{
    Created = 1,
    Planning = 2,
    WaitingForHuman = 3,
    Coding = 4,
    WaitingForExecution = 5,
    Executing = 6,
    Reviewing = 7,
    Fixing = 8,
    Completed = 9,
    Failed = 10,
    TimedOut = 11
}
