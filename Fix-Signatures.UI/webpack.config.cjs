const path = require("path");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");

module.exports = {
  mode: "production",
  entry: "./src/index.js",
  externalsType: "window",
  externals: {
    react: "React",
    "cs2/api": "cs2/api",
    "cs2/l10n": "cs2/l10n"
  },
  module: {
    rules: [
      {
        test: /\.css$/,
        use: [MiniCssExtractPlugin.loader, { loader: "css-loader", options: { modules: true } }]
      }
    ]
  },
  output: {
    path: path.resolve(__dirname, "dist"),
    filename: "Fix-Signatures.js",
    library: { type: "module" },
    publicPath: "coui://ui-mods/",
    clean: true
  },
  plugins: [new MiniCssExtractPlugin({ filename: "Fix-Signatures.css" })],
  experiments: { outputModule: true }
};
