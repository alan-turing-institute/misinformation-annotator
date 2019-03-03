WITH user_annotations AS (
    SELECT article_url, annotation FROM [annotations]
    WHERE user_id = @UserId
), 
training_articles AS (
    SELECT batch_article_test.article_url 
    FROM [batch_article_test] 
        LEFT JOIN user_annotations
        ON batch_article_test.article_url = user_annotations.article_url
    WHERE minibatch_id = 0 AND annotation IS NULL
)
SELECT TOP(1) articles_v5.article_url, title, site_name, plain_content 
FROM [articles_v5] 
WHERE articles_v5.article_url IN (SELECT article_url FROM training_articles)