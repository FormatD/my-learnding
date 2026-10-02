namespace Learning;

// Multiple success branches have distinct schemas; retain their original wire fields.
public record ExistingAttemptResponse(Attempt Attempt,Grading Grading,string AssessmentStatus);
public record NewAttemptResponse(Attempt Attempt,Grading Grading,string AssessmentStatus,string Feedback,string Explanation);
public record PDFSourceResponse(Source Source,BuilderRun Job);
