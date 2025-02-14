import Fastify from "fastify";

const app = Fastify();

app.get("/test/:id", async (req, rs) => {
  app.log.info("test route called");
  return { hello: "world" };
});

try {
  await app.listen({ port: 3000 });
} catch (err) {
  app.log.error(err);
  process.exit(1);
}
