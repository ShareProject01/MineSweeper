using UnityEngine;

// T には MonoBehaviour を継承したクラスだけが入るように制約 (where T : MonoBehaviour) をつけます
public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
	public static T Instance { get; private set; }

	protected virtual void Awake()
	{
		if (Instance == null)
		{
			// 自分自身を T 型としてインスタンスに代入
			Instance = this as T;

			// シーン跨ぎで保持したい場合はコメント解除
			// DontDestroyOnLoad(gameObject);
		}
		else
		{
			// すでに存在している場合は重複している自分を破棄
			Destroy(gameObject);
		}
	}
}