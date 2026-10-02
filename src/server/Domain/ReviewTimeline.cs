namespace Learning;
public record ReviewOccurrence(string Id,string TargetType,Guid TargetId,Guid? KCId,string Stage,DateOnly DueDate,Guid ScheduledByAttemptId,DateTimeOffset ScheduledAt,DateTimeOffset? ClosedAt,Guid? ExecutedAttemptId,DateTimeOffset? ExecutedAt);
// Reconstructed with the same effective grading/mapping inputs as assessment; no invented historical rows.
public sealed class ReviewTimeline
{
    public List<ReviewOccurrence> Items { get; }=[];
    readonly Dictionary<(string,Guid),int> active=[];
    public void Observe(Attempt attempt,Question question,StudyTask task,DateOnly day)
    {
        foreach(var index in active.Values.ToArray())
        {
            var row=Items[index];
            var matches=row.TargetType=="WrongQuestion"?row.TargetId==question.Id:task.Type=="Review" && task.ReviewTargetId==row.TargetId;
            if(matches && day>=row.DueDate && row.ExecutedAt==null)Items[index]=row with {ExecutedAttemptId=attempt.Id,ExecutedAt=attempt.CreatedAt};
        }
    }
    public void Synchronize(IEnumerable<Review> reviews,Attempt attempt)
    {
        foreach(var review in reviews)
        {
            var key=(review.TargetType,review.TargetId);
            if(active.TryGetValue(key,out var index))
            {
                var old=Items[index];
                if(review.Status=="Pending" && old.Stage==review.Stage && old.DueDate==review.DueDate && old.KCId==review.KCId)continue;
                Items[index]=old with {ClosedAt=attempt.CreatedAt};active.Remove(key);
            }
            if(review.Status!="Pending")continue;
            var id=$"{review.TargetType}:{review.TargetId:N}:{attempt.Id:N}:{Items.Count}";
            active[key]=Items.Count;
            Items.Add(new(id,review.TargetType,review.TargetId,review.KCId,review.Stage,review.DueDate,attempt.Id,attempt.CreatedAt,null,null,null));
        }
    }
}
