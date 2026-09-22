using System.IO;

namespace bms_editer.Models;

public sealed class BmsWavItem
{
    public string Key { get; set; } = string.Empty;

    // 재생에 쓰는 실제 경로. 적힌 자리에 파일이 없으면 하위 폴더에서 찾아 붙인 결과일 수 있다.
    public string FilePath { get; set; } = string.Empty;

    // 파일에 적혀 있던 경로 문자열 그대로. 저장할 때 되돌려 쓴다.
    // 새로 추가한 키음은 비어 있고, 그때는 FilePath 로 상대경로를 만든다.
    public string SourceText { get; set; } = string.Empty;

    // 적힌 자리에 파일이 없어서 같은 이름을 하위 폴더에서 찾아 붙였는지.
    // 그 추측 결과를 저장 파일에 박으면, 오래된 백업 폴더가 남아 있을 때
    // 차트가 그쪽을 가리키도록 조용히 바뀌어 버린다.
    public bool IsPathGuessed { get; set; }

    public string FileName => Path.GetFileName(FilePath);
    public string DisplayText => $"#{Key}: {FileName}";
}
